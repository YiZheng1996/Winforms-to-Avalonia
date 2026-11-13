using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 手工设备操作的共享安全边界。
///
/// 连接测试、点位诊断等手工动作必须先快速检查活动试验，进入与试验执行/
/// 配置应用相同的协调器后再次检查，避免检查与执行之间出现竞态窗口。
/// </summary>
public sealed class DeviceManualOperationGuard(
    DeviceOperationCoordinator operations,
    IRecordRepository records)
{
    private readonly DeviceOperationCoordinator _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    private readonly IRecordRepository _records = records ?? throw new ArgumentNullException(nameof(records));

    /// <summary>
    /// 在共享诊断门内执行一次手工设备动作。
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await EnsureManualOperationAllowedAsync(ct);
        await using var lease = await _operations.EnterDiagnosticsAsync(ct);
        await EnsureManualOperationAllowedAsync(ct);
        ct.ThrowIfCancellationRequested();
        return await action(ct);
    }

    /// <summary>
    /// 无返回值动作的便捷重载。
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await ExecuteAsync(
            async token =>
            {
                await action(token);
                return true;
            },
            ct);
    }

    private async Task EnsureManualOperationAllowedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 查询异常必须向调用方传播，不能把数据库故障误判为“没有活动试验”。
        if (await _records.GetActiveRunningRecordAsync(ct) is not null)
            throw new DomainException("试验运行中，手工设备操作已禁用");
    }
}
