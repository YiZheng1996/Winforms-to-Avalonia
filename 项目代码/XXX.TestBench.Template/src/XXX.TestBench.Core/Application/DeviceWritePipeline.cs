using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

public sealed record WriteCommand(
    UserContext Actor,
    DevicePoint Point,
    object? Value,
    DeviceMode Mode,
    IDeviceRuntime Runtime,
    bool ActiveRunOk,
    bool RiskConfirmed,
    Func<CancellationToken, Task>? InterlockCheck = null);

/// <summary>
/// 设备写入安全链：权限 → 模式（Hardware 必须真实运行时；Simulation 只写仿真状态）→
/// 连接与点位质量 → 活动任务/运行状态 → 业务联锁 → 风险确认 → 写入 → 回读(Hardware) → 审计。
/// 界面不得绕过本管道直接调用协议库写方法。
/// </summary>
public sealed class DeviceWritePipeline
{
    private readonly IAuditLog _audit;
    private readonly IAppLogger _logger;

    public DeviceWritePipeline(IAuditLog audit, IAppLogger logger)
    {
        _audit = audit;
        _logger = logger;
    }

    public async Task<PointValue> ExecuteAsync(WriteCommand command, CancellationToken ct = default)
    {
        var requiredPermission = command.Point.RiskLevel == WriteRiskLevel.HighRisk
            ? PermissionCode.CalibrateDevices
            : PermissionCode.ManualControl;
        try
        {
            command.Actor.EnsurePermission(requiredPermission);
        }
        catch (AuthorizationException)
        {
            await _audit.WriteAsync(command.Actor.LoginName, "AccessDenied", requiredPermission.ToString(), command.Point.Code);
            throw;
        }

        if (command.Mode == DeviceMode.Hardware)
        {
            if (command.Runtime.IsSimulation)
                throw new DomainException("Hardware 模式不得写入仿真运行时");
        }
        else if (!command.Runtime.IsSimulation)
        {
            throw new DomainException("Simulation 模式不得写入真实设备运行时");
        }

        var status = command.Runtime.Status;
        if (status.Health is not (DeviceHealth.Healthy or DeviceHealth.Degraded) || !status.IsConnected)
            throw new DomainException($"设备 {command.Runtime.Name} 未连接或状态异常（{status.Health}）");

        if (!command.Point.IsWritable)
            throw new DomainException($"点位 {command.Point.Code} 不可写");

        if (!command.ActiveRunOk)
            throw new DomainException("当前没有活动任务/运行状态，拒绝写入");

        if (command.InterlockCheck is not null)
            await command.InterlockCheck(ct);

        if (command.Point.RiskLevel == WriteRiskLevel.HighRisk && !command.RiskConfirmed)
            throw new DomainException($"高风险写入 {command.Point.Code} 需要二次确认");

        var written = await command.Runtime.WriteAsync(command.Point, command.Value, ct);

        if (command.Mode == DeviceMode.Hardware)
        {
            var readback = await command.Runtime.ReadAsync(command.Point, ct);
            if (readback.Quality != PointQuality.Good)
                throw new DomainException($"写入回读质量异常（{readback.Quality}）");
            if (!ValuesEqual(command.Value, readback.Value))
                throw new DomainException($"写入回读不一致：写入 {command.Value}，回读 {readback.Value}");
            await _audit.WriteAsync(command.Actor.LoginName, "DeviceWrite", command.Point.Code, $"value={command.Value} readback={readback.Value}", ct);
        }
        else
        {
            _logger.Info($"Simulation 写入 {command.Point.Code}={command.Value}");
        }

        return written;
    }

    private static bool ValuesEqual(object? expected, object? actual)
    {
        if (expected is null || actual is null) return Equals(expected, actual);
        if (expected is bool eb && actual is bool ab) return eb == ab;
        if (expected is string es && actual is string as2) return string.Equals(es, as2, StringComparison.Ordinal);
        if (decimal.TryParse(Convert.ToString(expected), out var ed) && decimal.TryParse(Convert.ToString(actual), out var ad))
            return Math.Abs(ed - ad) < 0.0001m;
        return Equals(expected, actual);
    }
}
