namespace XXX.TestBench.Core.Domain.Identity;

/// <summary>
/// 用户账号信息。
/// </summary>
public sealed class User
{
    /// <summary>
    /// 用户编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 登录名。
    /// </summary>
    public required string LoginName { get; init; }
    /// <summary>
    /// 显示姓名。
    /// </summary>
    public required string DisplayName { get; set; }
    /// <summary>
    /// 密码散列值。
    /// </summary>
    public required string PasswordHash { get; set; }
    /// <summary>
    /// 是否要求下次登录时修改密码。
    /// </summary>
    public bool MustChangePassword { get; set; }
    /// <summary>
    /// 是否启用该账号。
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>
    /// 连续登录失败次数。
    /// </summary>
    public int FailedLoginCount { get; set; }
    /// <summary>
    /// 账号锁定截止时间。
    /// </summary>
    public DateTime? LockedUntilUtc { get; set; }
    /// <summary>
    /// 所属角色编号。
    /// </summary>
    public int RoleId { get; set; }
    /// <summary>
    /// 账号创建时间。
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }
}
