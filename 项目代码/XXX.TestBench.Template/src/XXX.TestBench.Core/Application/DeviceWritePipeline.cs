using System.Globalization;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 一次写入请求所需的安全上下文。
/// </summary>
public sealed record WriteCommand(
    UserContext Actor,
    DevicePoint Point,
    object? Value,
    DeviceMode Mode,
    IDeviceRuntime Runtime,
    bool ActiveRunOk,
    bool RiskConfirmed,
    Func<CancellationToken, Task>? InterlockCheck = null,
    string? ExpectedRevision = null);

/// <summary>
/// 设备写入安全链：按 PointId 重新定位 → 权限 → 目标设备模式（Hardware 必须真实运行时；Simulation 只写仿真状态）→
/// 目标设备/通道可用 → 点位及输入类型/范围 → 活动任务/运行状态 → 业务联锁 → 风险确认 →
/// 逆换算 → 写入 → 新鲜回读 → 审计。
/// 界面不得绕过本管道直接调用协议库写方法。
/// </summary>
public sealed class DeviceWritePipeline
{
    private readonly IAuditLog _audit;
    private readonly IAppLogger _logger;
    private readonly DeviceOperationCoordinator? _operations;

    public DeviceWritePipeline(IAuditLog audit, IAppLogger logger, DeviceOperationCoordinator? operations = null)
    {
        _audit = audit;
        _logger = logger;
        _operations = operations;
    }

    /// <summary>
    /// 按安全链校验并执行一次点位写入。
    /// </summary>
    public async Task<PointValue> ExecuteAsync(WriteCommand command, CancellationToken ct = default)
    {
        if (_operations is null)
            return await ExecuteCoreAsync(command, ct);
        await using var lease = await _operations.EnterExecutionAsync(ct);
        return await ExecuteCoreAsync(command, ct);
    }

    private async Task<PointValue> ExecuteCoreAsync(WriteCommand command, CancellationToken ct)
    {
        var point = ResolveCurrentPoint(command);

        // 高风险点位需要校准权限，普通点位需要手动控制权限；权限判断不能信任旧对象中的风险等级。
        var requiredPermission = point.RiskLevel == WriteRiskLevel.HighRisk
            ? PermissionCode.CalibrateDevices
            : PermissionCode.ManualControl;
        try
        {
            command.Actor.EnsurePermission(requiredPermission);
        }
        catch (AuthorizationException)
        {
            await _audit.WriteAsync(command.Actor.LoginName, "AccessDenied", requiredPermission.ToString(), point.Code);
            throw;
        }

        // 只能使用目标点位所属设备的状态，不能用其他设备在线替代。
        var status = string.IsNullOrWhiteSpace(point.DeviceId)
            ? command.Runtime.Status
            : command.Runtime.GetDeviceStatus(point.DeviceId);
        ct.ThrowIfCancellationRequested();
        var targetMode = status.IsSimulation ? DeviceMode.Simulation : DeviceMode.Hardware;
        if (command.Mode != DeviceMode.Mixed && command.Mode != targetMode)
            throw new DomainException($"写入模式与目标设备不一致：请求 {command.Mode}，目标设备 {targetMode}");
        if (status.Health is not (DeviceHealth.Healthy or DeviceHealth.Degraded) || !status.IsConnected)
            throw new DomainException($"目标设备 {point.DeviceId} 未连接或状态异常（{status.Health}）");

        if (!point.IsWritable)
            throw new DomainException($"点位 {point.Code} 不可写");
        if (point.WritePolicy != PointWritePolicy.ReadBackEqual)
            throw new DomainException($"点位 {point.Code} 的写入策略不受支持：{point.WritePolicy}");

        if (!command.ActiveRunOk)
            throw new DomainException("当前没有活动任务/运行状态，拒绝写入");

        if (command.InterlockCheck is not null)
            await command.InterlockCheck(ct);

        if (point.RiskLevel == WriteRiskLevel.HighRisk && !command.RiskConfirmed)
            throw new DomainException($"高风险写入 {point.Code} 需要二次确认");

        var rawValue = DevicePointValueConverter.ToRaw(point, command.Value);
        var writeStarted = false;
        try
        {
            // 从写入调用开始，取消/超时都不能自动重发；结果按不确定处理。
            writeStarted = true;
            _ = await command.Runtime.WriteAsync(point, rawValue, ct);

            PointValue readback;
            try
            {
                readback = !string.IsNullOrWhiteSpace(point.PointId)
                    ? await command.Runtime.ReadFreshAsync(point.PointId, ct)
                    : await command.Runtime.ReadAsync(point, ct);
            }
            catch (OperationCanceledException)
            {
                throw new DeviceWriteUncertainException($"点位 {point.Code} 写入后回读被取消，结果不确定，禁止自动重发");
            }
            catch (TimeoutException ex)
            {
                throw new DeviceWriteUncertainException($"点位 {point.Code} 写入后回读超时，结果不确定，禁止自动重发：{ex.Message}");
            }
            catch (Exception ex)
            {
                throw new DeviceWriteUncertainException($"点位 {point.Code} 写入后无法完成新鲜回读，结果不确定，禁止自动重发：{ex.Message}");
            }

            if (readback.Quality != PointQuality.Good)
                throw new DomainException($"写入回读质量异常（{readback.Quality}）");
            if (!string.IsNullOrWhiteSpace(readback.PointId)
                && !string.Equals(readback.PointId, point.PointId, StringComparison.OrdinalIgnoreCase))
                throw new DeviceWriteUncertainException($"点位 {point.Code} 回读返回了错误的 PointId，结果不确定，禁止自动重发");
            var readbackRawValue = readback.RawValue ?? readback.Value;
            if (!DevicePointValueConverter.RawValuesEqual(point, rawValue, readbackRawValue))
                throw new DomainException($"写入回读不一致：原始值 {rawValue}，回读 {readbackRawValue}");

            var detail = $"deviceId={point.DeviceId};pointId={point.PointId};revision={command.Runtime.ActiveRevision};engineering={FormatValue(command.Value)};raw={FormatValue(rawValue)};result=Success;readback={FormatValue(readback.Value)}";
            try
            {
                // 审计不受写入取消令牌影响；失败时明确暴露“已发出但未审计”，禁止自动重发。
                await _audit.WriteAsync(command.Actor.LoginName, "DeviceWrite", point.Code, detail, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Error($"设备写入已完成但审计失败：{point.Code}", ex);
                throw new DeviceWriteAuditException($"点位 {point.Code} 写入已执行，但审计记录失败；请人工核对，禁止自动重发");
            }

            _logger.Info($"{command.Mode} 写入 {point.Code} engineering={FormatValue(command.Value)} raw={FormatValue(rawValue)}");
            // 对外返回经过本次新鲜回读确认的结果，避免把驱动写入调用返回的旧/未确认值交给上层。
            return readback;
        }
        catch (OperationCanceledException)
        {
            if (writeStarted)
            {
                var uncertain = new DeviceWriteUncertainException($"点位 {point.Code} 写入结果不确定，禁止自动重发");
                await WriteFailureAuditSafeAsync(command, point, rawValue, uncertain);
                throw uncertain;
            }
            throw;
        }
        catch (TimeoutException ex)
        {
            var uncertain = new DeviceWriteUncertainException($"点位 {point.Code} 写入超时，结果不确定，禁止自动重发：{ex.Message}");
            await WriteFailureAuditSafeAsync(command, point, rawValue, uncertain);
            throw uncertain;
        }
        catch (Exception ex)
        {
            if (writeStarted)
                await WriteFailureAuditSafeAsync(command, point, rawValue, ex);
            throw;
        }
    }

    private static DevicePoint ResolveCurrentPoint(WriteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command.Point);
        ArgumentNullException.ThrowIfNull(command.Runtime);
        var requested = command.Point;
        if (string.IsNullOrWhiteSpace(requested.PointId))
        {
            if (!string.IsNullOrWhiteSpace(command.Runtime.ActiveRevision))
                throw new DomainException("当前运行时要求使用带 PointId 的生效点位对象，拒绝旧点位对象");
            return requested;
        }

        var current = command.Runtime.GetPoint(requested.PointId)
            ?? throw new DomainException($"点位 {requested.PointId} 不在当前生效运行时");
        if (!string.IsNullOrWhiteSpace(requested.DeviceId)
            && !string.Equals(requested.DeviceId, current.DeviceId, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"点位 {requested.PointId} 的设备归属已变化，拒绝写入");
        if (!string.IsNullOrWhiteSpace(requested.Revision)
            && !string.IsNullOrWhiteSpace(current.Revision)
            && !string.Equals(requested.Revision, current.Revision, StringComparison.Ordinal))
            throw new DomainException($"点位 {requested.PointId} 的配置版本已过期，拒绝写入");
        if (!string.IsNullOrWhiteSpace(current.Revision)
            && !string.IsNullOrWhiteSpace(command.Runtime.ActiveRevision)
            && !string.Equals(current.Revision, command.Runtime.ActiveRevision, StringComparison.Ordinal))
            throw new DomainException($"点位 {requested.PointId} 与当前生效配置版本不一致，拒绝写入");
        if (!string.IsNullOrWhiteSpace(command.ExpectedRevision)
            && !string.Equals(command.ExpectedRevision, command.Runtime.ActiveRevision, StringComparison.Ordinal))
            throw new DomainException("写入请求配置版本已过期，拒绝写入");
        if (!string.IsNullOrWhiteSpace(current.Revision)
            && string.IsNullOrWhiteSpace(requested.Revision))
            throw new DomainException($"点位 {requested.PointId} 缺少配置版本，拒绝使用可能过期的对象");
        return current;
    }

    private static string FormatValue(object? value)
        => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";

    private async Task WriteFailureAuditSafeAsync(
        WriteCommand command,
        DevicePoint point,
        object? rawValue,
        Exception error)
    {
        var action = error is DeviceWriteUncertainException ? "DeviceWriteUncertain" : "DeviceWriteFailed";
        var detail = $"deviceId={point.DeviceId};pointId={point.PointId};revision={command.Runtime.ActiveRevision};engineering={FormatValue(command.Value)};raw={FormatValue(rawValue)};result={action};error={error.Message}";
        try { await _audit.WriteAsync(command.Actor.LoginName, action, point.Code, detail, CancellationToken.None); }
        catch (Exception auditError) { _logger.Error($"设备写入失败审计也失败：{point.Code}", auditError); }
    }
}
