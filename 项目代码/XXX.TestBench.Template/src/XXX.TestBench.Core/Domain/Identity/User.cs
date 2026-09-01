namespace XXX.TestBench.Core.Domain.Identity;

public sealed class User
{
    public int Id { get; set; }
    public required string LoginName { get; init; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public int RoleId { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}
