using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 报表记录表的 SQLite 映射实体。
/// </summary>
[Table(Name = "report_records")]
internal sealed class SqliteReportRecord
{
    /// <summary>
    /// 报表记录主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 关联的试验记录编号。
    /// </summary>
    [Column(Name = "test_record_id")]
    public int TestRecordId { get; set; }

    /// <summary>
    /// 报表模板路径。
    /// </summary>
    [Column(Name = "template_path")]
    public string TemplatePath { get; set; } = string.Empty;

    /// <summary>
    /// 报表输出路径，生成前为空。
    /// </summary>
    [Column(Name = "output_path", IsNullable = true)]
    public string? OutputPath { get; set; }

    /// <summary>
    /// 报表生成状态枚举值。
    /// </summary>
    [Column(Name = "status")]
    public int Status { get; set; }

    /// <summary>
    /// 报表生成错误信息，成功或未出错时为空。
    /// </summary>
    [Column(Name = "error", IsNullable = true)]
    public string? Error { get; set; }

    /// <summary>
    /// 创建报表记录的用户编号。
    /// </summary>
    [Column(Name = "created_by_user_id")]
    public int CreatedByUserId { get; set; }

    /// <summary>
    /// 创建时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "created_at_utc")]
    public string CreatedAtUtc { get; set; } = string.Empty;

    /// <summary>
    /// 报表完成时间，未完成时为空。
    /// </summary>
    [Column(Name = "completed_at_utc", IsNullable = true)]
    public string? CompletedAtUtc { get; set; }

    [Column(Name = "revision_id", IsNullable = true)] public string? RevisionId { get; set; }
    [Column(Name = "revision_no", IsNullable = true)] public int? RevisionNo { get; set; }
    [Column(Name = "trigger", IsNullable = true)] public string? Trigger { get; set; }
    [Column(Name = "owner_node_id", IsNullable = true)] public string? OwnerNodeId { get; set; }
    [Column(Name = "attempt_token", IsNullable = true)] public string? AttemptToken { get; set; }
    [Column(Name = "active_attempt_key", IsNullable = true)] public string? ActiveAttemptKey { get; set; }
    [Column(Name = "storage_key", IsNullable = true)] public string? StorageKey { get; set; }
    [Column(Name = "template_hash", IsNullable = true)] public string? TemplateHash { get; set; }
    [Column(Name = "mapping_hash", IsNullable = true)] public string? MappingHash { get; set; }
    [Column(Name = "input_hash", IsNullable = true)] public string? InputHash { get; set; }
    [Column(Name = "manifest_hash", IsNullable = true)] public string? ManifestHash { get; set; }
    [Column(Name = "engine_name", IsNullable = true)] public string? EngineName { get; set; }
    [Column(Name = "engine_version", IsNullable = true)] public string? EngineVersion { get; set; }
    [Column(Name = "calculation_result_json", IsNullable = true)] public string? CalculationResultJson { get; set; }
    [Column(Name = "error_stage", IsNullable = true)] public string? ErrorStage { get; set; }
    [Column(Name = "request_id", IsNullable = true)] public string? RequestId { get; set; }
    [Column(Name = "purpose", IsNullable = true)] public int? Purpose { get; set; }
    [Column(Name = "row_version")] public long RowVersion { get; set; }
    [Column(Name = "pdf_storage_key", IsNullable = true)] public string? PdfStorageKey { get; set; }
    [Column(Name = "pdf_artifact_id", IsNullable = true)] public string? PdfArtifactId { get; set; }
    [Column(Name = "pdf_hash", IsNullable = true)] public string? PdfHash { get; set; }
    [Column(Name = "pdf_manifest_key", IsNullable = true)] public string? PdfManifestKey { get; set; }
    [Column(Name = "pdf_state")] public int PdfState { get; set; }
}
