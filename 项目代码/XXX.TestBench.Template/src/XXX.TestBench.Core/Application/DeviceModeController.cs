using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

public sealed record DeviceModeResult(bool Ok, string? Error);

/// <summary>
/// 设备模式控制：DeviceMode 是启动配置；切换要求维护权限、无活动试验并重新初始化运行时。
/// Hardware 初始化/连接/轮询失败时保留 Hardware 模式并进入 Faulted，禁止自动回退 Simulation。
/// </summary>
public sealed class DeviceModeController
{
    private readonly IDeviceRuntimeFactory _factory;
    private readonly IAppLogger _logger;
    private readonly IAuditLog _audit;

    public DeviceModeController(IDeviceRuntimeFactory factory, IAppLogger logger, IAuditLog audit)
    {
        _factory = factory;
        _logger = logger;
        _audit = audit;
    }

    public DeviceMode CurrentMode { get; private set; } = DeviceMode.Simulation;
    public IDeviceRuntime? Runtime { get; private set; }
    public DeviceHealth Health { get; private set; } = DeviceHealth.Unknown;
    public string? LastError { get; private set; }

    public async Task<DeviceModeResult> InitializeAsync(DeviceMode mode, CancellationToken ct = default)
    {
        await DisposeRuntimeAsync();
        CurrentMode = mode;
        try
        {
            Runtime = await _factory.CreateAsync(mode, ct);
            await Runtime.StartAsync(ct);
            Health = DeviceHealth.Healthy;
            LastError = null;
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

    public async Task<DeviceModeResult> SwitchModeAsync(UserContext actor, DeviceMode mode, bool hasActiveTask, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageDevices);
        if (hasActiveTask) return new DeviceModeResult(false, "存在活动试验，禁止切换设备模式");
        if (mode == CurrentMode) return new DeviceModeResult(false, $"当前已是 {mode} 模式");
        return await InitializeAsync(mode, ct);
    }

    public async Task<DeviceModeResult> ReconnectAsync(CancellationToken ct = default)
    {
        if (Runtime is null) return new DeviceModeResult(false, "设备运行时未初始化");
        try
        {
            await Runtime.StopAsync(ct);
            await Runtime.StartAsync(ct);
            Health = DeviceHealth.Healthy;
            LastError = null;
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

    private async Task DisposeRuntimeAsync()
    {
        if (Runtime is not null)
        {
            await Runtime.StopAsync();
            await Runtime.DisposeAsync();
            Runtime = null;
        }
    }

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
