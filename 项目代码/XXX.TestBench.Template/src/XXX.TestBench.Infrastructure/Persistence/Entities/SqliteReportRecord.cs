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
}
