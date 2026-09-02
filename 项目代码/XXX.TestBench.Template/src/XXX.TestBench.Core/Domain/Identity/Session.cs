namespace XXX.TestBench.Core.Domain.Identity;

/// <summary>
/// 登录会话。撤销或过期后不可再用于权限校验。
/// </summary>
public sealed class Session
{
    public required string Token { get; init; }
    public int UserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActiveAt(DateTime utcNow) => RevokedAtUtc is null && utcNow < ExpiresAtUtc;
}
