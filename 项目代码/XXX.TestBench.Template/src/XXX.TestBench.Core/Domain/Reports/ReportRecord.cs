namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>报表生成记录。报表生成失败不回滚已提交的试验记录，可重试并留日志。</summary>
public sealed class ReportRecord
{
    public int Id { get; set; }
    public int TestRecordId { get; init; }
    public required string TemplatePath { get; init; }
    public string? OutputPath { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    public string? Error { get; set; }
    public int CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; set; }
}
