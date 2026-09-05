using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 项目级试验参数表的 SQLite 映射实体。
/// </summary>
[Table(Name = "project_test_parameters")]
internal sealed class SqliteProjectTestParameter
{
    /// <summary>
    /// 参数作用域键，同时也是主键。
    /// </summary>
    [Column(Name = "scope_key", IsPrimary = true)]
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>
    /// 试验持续时间，单位为秒。
    /// </summary>
    [Column(Name = "test_time_seconds")]
    public int TestTimeSeconds { get; set; }

    /// <summary>
    /// 最后修改参数的用户登录名。
    /// </summary>
    [Column(Name = "updated_by")]
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 最后更新时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = string.Empty;
}
