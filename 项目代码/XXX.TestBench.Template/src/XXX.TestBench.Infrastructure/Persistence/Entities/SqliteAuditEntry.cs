using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 审计日志表的 SQLite 映射实体。
/// </summary>
[Table(Name = "audit_logs")]
internal sealed class SqliteAuditEntry
{
    /// <summary>
    /// 审计日志主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 执行动作的操作者标识。
    /// </summary>
    [Column(Name = "actor")]
    public string Actor { get; set; } = string.Empty;

    /// <summary>
    /// 审计动作编码。
    /// </summary>
    [Column(Name = "action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 被操作对象标识，可为空。
    /// </summary>
    [Column(Name = "target", IsNullable = true)]
    public string? Target { get; set; }

    /// <summary>
    /// 动作详细信息，可为空。
    /// </summary>
    [Column(Name = "detail", IsNullable = true)]
    public string? Detail { get; set; }

    /// <summary>
    /// 记录创建时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "created_at_utc")]
    public string CreatedAtUtc { get; set; } = string.Empty;
}
