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
/// 设备模式控制：DeviceMode 是启动配置；切换要求维护权限、无活动试验并重新初始化运行时。
/// Hardware 初始化/连接/轮询失败时保留 Hardware 模式并进入 Faulted，禁止自动回退 Simulation。
/// </summary>
public sealed class DeviceModeController
{
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
    /// 按指定模式创建并启动设备运行时。
    /// </summary>
    public async Task<DeviceModeResult> InitializeAsync(DeviceMode mode, CancellationToken ct = default)
    {
        await DisposeRuntimeAsync();
        CurrentMode = mode;
        try
        {
            Runtime = await _factory.CreateAsync(mode, ct);
            await Runtime.StartAsync(ct);
            Health = Runtime.Status.Health;
            LastError = Runtime.Status.LastError;
            if (Health is DeviceHealth.Faulted or DeviceHealth.Unknown)
            {
                var error = string.IsNullOrWhiteSpace(LastError)
                    ? $"设备模式 {mode} 启动后没有可用设备"
                    : LastError;
                await _audit.WriteAsync("system", "DeviceModeInitFailed", mode.ToString(), error, ct);
                return new DeviceModeResult(false, error);
            }
            await _audit.WriteAsync("system", "DeviceModeInitialized", mode.ToString(), null, ct);
            return new DeviceModeResult(true, null);
        }
        catch (Exception ex)
        {
            Health = DeviceHealth.Faulted;
            LastError = ex.Message;
            _logger.Error($"设备模式 {mode} 初始化失败", ex);
            await _audit.WriteAsync("system", "DeviceModeInitFailed", mode.ToString(), ex.Message, ct);
            // 保留 mode，不创建 Simulation 运行时，不伪造成功数据
            return new DeviceModeResult(false, $"设备模式 {mode} 初始化失败：{ex.Message}");
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
                return new DeviceModeResult(false, LastError ?? "设备重连后没有可用设备");
            return new DeviceModeResult(true, null);
        }
        catch (Exception ex)
        {
            Health = DeviceHealth.Faulted;
            LastError = ex.Message;
            _logger.Error("设备重连失败", ex);
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
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止并释放当前运行时。
    /// </summary>
    private async Task DisposeRuntimeAsync()
    {
        if (Runtime is not null)
        {
            await Runtime.StopAsync();
            await Runtime.DisposeAsync();
            Runtime = null;
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
