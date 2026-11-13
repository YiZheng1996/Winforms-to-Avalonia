using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 受 OperationId、设备 Revision、连接代次和允许信号集合约束的设备访问边界。
/// 实现不得向执行器暴露协议客户端或原始 IDeviceRuntime。
/// </summary>
public interface IBenchDeviceAccess
{
    Task<PointValue> ReadFreshAsync(string signalKey, CancellationToken cancellationToken = default);

    Task<BenchDeviceWriteResult> WriteAsync(
        string signalKey,
        object value,
        CancellationToken cancellationToken = default);
}

public sealed record BenchDeviceWriteResult(
    string CommandId,
    BenchDeviceWriteDisposition Disposition,
    PointValue? ReadBack,
    string Message,
    string? EvidenceKey);

/// <summary>
/// 设备写入结果分类；Uncertain 表示命令可能已发出但没有可信回读。
/// </summary>
public enum BenchDeviceWriteDisposition
{
    Rejected = 0,
    Confirmed = 1,
    Uncertain = 2
}
