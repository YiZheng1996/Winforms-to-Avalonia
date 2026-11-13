namespace XXX.TestBench.Core.Domain.Identity;

/// <summary>
/// 登录会话。撤销或过期后不可再用于权限校验。
/// </summary>
public sealed class Session
{
    /// <summary>
    /// 会话令牌。
    /// </summary>
    public required string Token { get; init; }
    /// <summary>
    /// 所属用户编号。
    /// </summary>
    public int UserId { get; init; }
    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }
    /// <summary>
    /// 过期时间。
    /// </summary>
    public DateTime ExpiresAtUtc { get; init; }
    /// <summary>
    /// 撤销时间，未撤销为空。
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// 判断会话在指定时刻是否仍有效。
    /// </summary>
    public bool IsActiveAt(DateTime utcNow) => RevokedAtUtc is null && utcNow < ExpiresAtUtc;
}
