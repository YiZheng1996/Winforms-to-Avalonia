using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 报表记录的数据仓库接口。
/// </summary>
public interface IReportRepository
{
    /// <summary>
    /// 在终态记录与本次冻结输入已处于当前事务时，幂等创建 Pending 报表任务。
    /// </summary>
    Task<ReportRecord> CreatePendingAsync(ReportRecord record, CancellationToken ct = default);
    /// <summary>
    /// 按请求标识查找报表，用于提交结果未知后的查证和幂等重试。
    /// </summary>
    Task<ReportRecord?> GetByRequestIdAsync(string requestId, CancellationToken ct = default);
    /// <summary>
    /// 按编号读取报表记录。
    /// </summary>
    Task<ReportRecord?> GetAsync(int id, CancellationToken ct = default);
    /// <summary>
    /// 按试验记录读取其全部报表记录。
    /// </summary>
    Task<IReadOnlyList<ReportRecord>> ListByRecordAsync(int testRecordId, CancellationToken ct = default);
    /// <summary>
    /// 列出本节点等待领取或等待恢复确认的任务。
    /// </summary>
    Task<IReadOnlyList<ReportRecord>> ListRecoverableAsync(string ownerNodeId, CancellationToken ct = default);
    /// <summary>
    /// 以条件更新领取 Pending 任务；不允许跨节点领取。
    /// </summary>
    Task<bool> TryClaimAsync(
        string revisionId,
        string ownerNodeId,
        ReportStatus expectedStatus,
        string attemptToken,
        CancellationToken ct = default);
    /// <summary>
    /// 以尝试令牌和输入摘要提交完整产物；报表状态、产品判定及主记录当前版本指针在同一事务更新。
    /// </summary>
    Task<bool> TryCompleteAsync(
        ReportRecord result,
        string attemptToken,
        string inputHash,
        EvaluationState evaluationState,
        string? evaluationText,
        string conclusionSource,
        CancellationToken ct = default);
    /// <summary>
    /// 以尝试令牌和输入摘要提交失败状态，迟到 worker 不能覆盖其他尝试。
    /// </summary>
    Task<bool> TryFailAsync(ReportRecord result, string attemptToken, string inputHash, CancellationToken ct = default);
}
