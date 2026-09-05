using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 用户表的 SQLite 映射实体。
/// </summary>
[Table(Name = "users")]
internal sealed class SqliteUser
{
    /// <summary>
    /// 用户主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 登录名。
    /// </summary>
    [Column(Name = "login_name")]
    public string LoginName { get; set; } = string.Empty;

    /// <summary>
    /// 用户显示名称。
    /// </summary>
    [Column(Name = "display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 密码散列值。
    /// </summary>
    [Column(Name = "password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 是否要求下次登录时修改密码，1 表示是。
    /// </summary>
    [Column(Name = "must_change_password")]
    public int MustChangePassword { get; set; }

    /// <summary>
    /// 账号是否启用，1 表示启用。
    /// </summary>
    [Column(Name = "is_enabled")]
    public int IsEnabled { get; set; }

    /// <summary>
    /// 连续登录失败次数。
    /// </summary>
    [Column(Name = "failed_login_count")]
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// 账号锁定截止时间，使用 UTC 文本保存；未锁定时为空。
    /// </summary>
    [Column(Name = "locked_until_utc", IsNullable = true)]
    public string? LockedUntilUtc { get; set; }

    /// <summary>
    /// 用户所属角色编号。
    /// </summary>
    [Column(Name = "role_id")]
    public int RoleId { get; set; }

    /// <summary>
    /// 创建时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "created_at_utc")]
    public string CreatedAtUtc { get; set; } = string.Empty;
}
