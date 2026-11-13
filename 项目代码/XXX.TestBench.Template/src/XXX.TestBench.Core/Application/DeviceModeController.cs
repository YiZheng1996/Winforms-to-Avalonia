using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 设备模式切换或初始化结果。
/// </summary>
public sealed record DeviceModeResult(bool Ok, string? Error);

/// <summary>
/// 设备运行时控制：设备模式由每台设备配置，控制器只维护当前运行时的聚合状态。
/// Hardware 初始化/连接/轮询失败时保留故障状态，禁止自动回退 Simulation。
/// </summary>
public sealed class DeviceModeController
{
    public event EventHandler? StateChanged;
    /// <summary>
    /// 运行时工厂。
    /// </summary>
    private readonly IDeviceRuntimeFactory _factory;
    /// <summary>
    /// 日志记录器。
    /// </summary>
    private readonly IAppLogger _logger;
    /// <summary>
    /// 审计日志。
    /// </summary>
    private readonly IAuditLog _audit;

    /// <summary>
    /// 创建设备模式控制器。
    /// </summary>
    public DeviceModeController(IDeviceRuntimeFactory factory, IAppLogger logger, IAuditLog audit)
    {
        _factory = factory;
        _logger = logger;
        _audit = audit;
    }

    /// <summary>
    /// 当前设备模式。
    /// </summary>
    public DeviceMode CurrentMode { get; private set; } = DeviceMode.Simulation;
    /// <summary>
    /// 当前设备运行时。
    /// </summary>
    public IDeviceRuntime? Runtime { get; private set; }
    /// <summary>
    /// 当前设备健康状态。
    /// </summary>
    public DeviceHealth Health { get; private set; } = DeviceHealth.Unknown;
    /// <summary>
    /// 最近一次错误信息。
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 按设备配置创建并启动设备运行时。
    /// </summary>
    public Task<DeviceModeResult> InitializeAsync(CancellationToken ct = default)
        => InitializeCoreAsync(null, ct);

    /// <summary>
    /// 旧的显式模式初始化入口，仅供旧基础设施测试/调用方过渡；页面不再提供全局切换。
    /// </summary>
    public Task<DeviceModeResult> InitializeAsync(DeviceMode mode, CancellationToken ct = default)
        => InitializeCoreAsync(mode, ct);

    private async Task<DeviceModeResult> InitializeCoreAsync(DeviceMode? requestedMode, CancellationToken ct)
    {
        await DisposeRuntimeAsync();
        var auditMode = requestedMode?.ToString() ?? "PerDevice";
        if (requestedMode is { } explicitMode)
            CurrentMode = explicitMode;
        try
        {
            Runtime = requestedMode is { } mode
                ? await _factory.CreateAsync(mode, ct)
                : await _factory.CreateAsync(ct);
            CurrentMode = Runtime.Mode;
            await Runtime.StartAsync(ct);
            Health = Runtime.Status.Health;
            LastError = Runtime.Status.LastError;
            if (Health is DeviceHealth.Faulted or DeviceHealth.Unknown)
            {
                var error = string.IsNullOrWhiteSpace(LastError)
                    ? "按设备配置启动后没有可用设备"
                    : LastError;
                await _audit.WriteAsync("system", "DeviceModeInitFailed", auditMode, error, ct);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return new DeviceModeResult(false, error);
            }
            await _audit.WriteAsync("system", "DeviceModeInitialized", auditMode, null, ct);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new DeviceModeResult(true, null);
        }
        catch (Exception ex)
        {
            Health = DeviceHealth.Faulted;
            LastError = ex.Message;
            _logger.Error($"设备运行时初始化失败（{auditMode}）", ex);
            await _audit.WriteAsync("system", "DeviceModeInitFailed", auditMode, ex.Message, ct);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new DeviceModeResult(false, $"设备运行时初始化失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 切换设备模式；无管理权限或有活动试验时拒绝。
    /// </summary>
    public async Task<DeviceModeResult> SwitchModeAsync(UserContext actor, DeviceMode mode, bool hasActiveTask, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageDevices);
        if (hasActiveTask) return new DeviceModeResult(false, "存在活动试验，禁止切换设备模式");
        if (mode == CurrentMode) return new DeviceModeResult(false, $"当前已是 {mode} 模式");
        return await InitializeAsync(mode, ct);
    }

    /// <summary>
    /// 重新启动当前运行时以恢复连接。
    /// </summary>
    public async Task<DeviceModeResult> ReconnectAsync(CancellationToken ct = default)
    {
        if (Runtime is null) return new DeviceModeResult(false, "设备运行时未初始化");
        try
        {
            await Runtime.StopAsync(ct);
            await Runtime.StartAsync(ct);
            Health = Runtime.Status.Health;
            LastError = Runtime.Status.LastError;
            if (Health is DeviceHealth.Faulted or DeviceHealth.Unknown)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                return new DeviceModeResult(false, LastError ?? "设备重连后没有可用设备");
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new DeviceModeResult(true, null);
        }
        catch (Exception ex)
        {
            Health = DeviceHealth.Faulted;
            LastError = ex.Message;
            _logger.Error("设备重连失败", ex);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new DeviceModeResult(false, $"重连失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 发布一个已经由完整配置应用服务启动并验收过的运行时实例。
    /// 该方法只替换门面引用，不停止或释放旧实例；旧实例的清理由配置应用事务负责。
    /// </summary>
    public Task PublishStartedRuntimeAsync(IDeviceRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Runtime = runtime;
        CurrentMode = runtime.Mode;
        Health = runtime.Status.Health;
        LastError = runtime.Status.LastError;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置切换回滚失败时，把当前控制器置为故障态，避免页面继续把旧运行时显示为可用。
    /// </summary>
    public void MarkRuntimeFaulted(string error)
    {
        Health = DeviceHealth.Faulted;
        LastError = string.IsNullOrWhiteSpace(error) ? "设备运行时恢复失败" : error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 应用退出或测试结束时停止并释放当前设备运行时，收敛轮询任务与通道资源。
    /// </summary>
    public async Task ShutdownAsync()
    {
        await DisposeRuntimeAsync().ConfigureAwait(false);
        Health = DeviceHealth.Unknown;
        LastError = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 停止并释放当前运行时。
    /// </summary>
    private async Task DisposeRuntimeAsync()
    {
        if (Runtime is not { } runtime)
            return;

        try
        {
            // 运行时停止可能要等待后台轮询；不把资源回收的延续投递回 Avalonia UI 线程，
            // 避免窗口关闭/测试 teardown 阶段因 UI Dispatcher 不再泵消息而死锁。
            await runtime.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            try { await runtime.DisposeAsync().ConfigureAwait(false); }
            finally { Runtime = null; }
        }
    }

    /// <summary>
    /// 校验权限，越权时写入审计并抛出异常。
    /// </summary>
    private void Ensure(UserContext actor, Core.Domain.Identity.PermissionCode permission)
    {
        try { actor.EnsurePermission(permission); }
        catch (AuthorizationException)
        {
            _ = _audit.WriteAsync(actor.LoginName, "AccessDenied", permission.ToString(), null);
            throw;
        }
    }
}
