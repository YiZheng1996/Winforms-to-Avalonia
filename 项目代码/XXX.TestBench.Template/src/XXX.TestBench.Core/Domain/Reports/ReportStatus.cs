namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>
/// 报表生成状态。
/// </summary>
public enum ReportStatus
{
    /// <summary>
    /// 等待生成。
    /// </summary>
    Pending = 0,
    /// <summary>
    /// 已生成。
    /// </summary>
    Completed = 1,
    /// <summary>
    /// 生成失败。
    /// </summary>
    Failed = 2
}
