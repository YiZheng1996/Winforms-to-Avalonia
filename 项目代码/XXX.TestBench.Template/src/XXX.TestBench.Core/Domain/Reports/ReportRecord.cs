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
    /// 不可变报告版本标识；旧记录为空。
    /// </summary>
    public string? RevisionId { get; init; }
    /// <summary>
    /// 同一试验记录内由持久化事务分配的递增版本号。
    /// </summary>
    public int? RevisionNo { get; set; }
    /// <summary>
    /// Automatic/Manual 等固定触发代码；旧记录为空。
    /// </summary>
    public string? Trigger { get; init; }
    public string? OwnerNodeId { get; init; }
    public string? AttemptToken { get; set; }
    public string? ActiveAttemptKey { get; set; }
    public string? StorageKey { get; set; }
    public string? TemplateHash { get; init; }
    public string? MappingHash { get; init; }
    public string? InputHash { get; init; }
    public string? ManifestHash { get; set; }
    public string? EngineName { get; set; }
    public string? EngineVersion { get; set; }
    public string? CalculationResultJson { get; set; }
    public string? ErrorStage { get; set; }
    public string? RequestId { get; init; }
    public ReportPurpose? Purpose { get; init; }
    public long RowVersion { get; set; }
    public string? PdfStorageKey { get; set; }
    public string? PdfArtifactId { get; set; }
    public string? PdfHash { get; set; }
    public string? PdfManifestKey { get; set; }
    public PdfState PdfState { get; set; } = PdfState.NotRequested;
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
