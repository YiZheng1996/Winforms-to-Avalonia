namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>
/// 报表生成记录。报表生成失败不回滚已提交的试验记录，可重试并留日志。
/// </summary>
public sealed class ReportRecord
{
    /// <summary>
    /// 报表记录编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 所属试验记录编号。
    /// </summary>
    public int TestRecordId { get; init; }
    /// <summary>
    /// 报表模板路径。
    /// </summary>
    public required string TemplatePath { get; init; }
    /// <summary>
    /// 生成后的文件路径。
    /// </summary>
    public string? OutputPath { get; set; }
    /// <summary>
    /// 生成状态。
    /// </summary>
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    /// <summary>
    /// 生成失败原因。
    /// </summary>
    public string? Error { get; set; }
    /// <summary>
    /// 发起生成的用户编号。
    /// </summary>
    public int CreatedByUserId { get; init; }
    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }
    /// <summary>
    /// 完成时间。
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }
}
