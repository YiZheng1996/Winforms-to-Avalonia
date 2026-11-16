using XXX.TestBench.Core.Domain.Execution;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 单台架持久占用仓储。所有阶段变化均使用 RowVersion 条件写入，不能靠查询后无条件覆盖。
/// </summary>
public interface IBenchControlStateStore
{
    Task<BenchControlState?> GetAsync(string benchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新建时 expectedRowVersion 为空且仅允许从 Idle 行领取；更新时必须传入当前版本并递增一。
    /// 释放操作必须写入本次操作的安全收尾结果和证据键。
    /// </summary>
    Task<bool> TrySaveAsync(
        BenchControlState next,
        long? expectedRowVersion,
        CancellationToken cancellationToken = default);
}
