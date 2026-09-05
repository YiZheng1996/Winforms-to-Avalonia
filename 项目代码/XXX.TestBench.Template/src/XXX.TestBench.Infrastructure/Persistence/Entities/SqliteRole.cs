using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 角色表的 SQLite 映射实体。
/// </summary>
[Table(Name = "roles")]
internal sealed class SqliteRole
{
    /// <summary>
    /// 角色主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 角色名称。
    /// </summary>
    [Column(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 系统内置角色标识；自定义角色时为空。
    /// </summary>
    [Column(Name = "system_key", IsNullable = true)]
    public string? SystemKey { get; set; }
}
