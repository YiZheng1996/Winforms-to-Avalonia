using XXX.TestBench.Core.Domain.Execution;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 安装级操作证据边界。恢复与清除必须按 OperationId 和 Revision 条件执行。
/// </summary>
public interface IOperationEvidenceStore
{
    Task<OperationFence?> ReadAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationFence>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(
        OperationFence next,
        long? expectedRevision,
        CancellationToken cancellationToken = default);

    Task ClearAfterSafeStopAsync(
        Guid operationId,
        string benchId,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}
