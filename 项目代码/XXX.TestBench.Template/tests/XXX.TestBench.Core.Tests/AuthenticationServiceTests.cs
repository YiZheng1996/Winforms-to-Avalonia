using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class AuthenticationServiceTests
{
    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => $"H:{password}";
        public bool Verify(string password, string hash) => hash == $"H:{password}";
    }

    private static (AuthenticationService Service, FakeUserRepository Users, FakeSessionManager Sessions, FixedClock Clock, FakeAuditLog Audit) Create()
    {
        var clock = new FixedClock();
        var users = new FakeUserRepository();
        var audit = new FakeAuditLog();
        var sessions = new FakeSessionManager();
        users.Roles.Add(new Role { Id = 1, Name = "Administrator" });
        var hasher = new PlainHasher();
        var service = new AuthenticationService(users, hasher, sessions, clock, audit);
        return (service, users, sessions, clock, audit);
    }

    [Fact]
    public async Task Login_Success_ReturnsSessionAndAudits()
    {
        var (service, users, sessions, _, audit) = Create();
        await users.AddAsync(new User { Id = 1, LoginName = "admin", DisplayName = "管理员", PasswordHash = "H:admin123", RoleId = 1, CreatedAtUtc = DateTime.UtcNow });

        var result = await service.LoginAsync("admin", "admin123");

        Assert.True(result.Success);
        Assert.NotNull(result.Session);
        var active = await sessions.GetActiveAsync(result.Session!.Token);
        Assert.NotNull(active);
        Assert.Contains(audit.Entries, e => e.Contains("LoginSucceeded"));
    }

    [Fact]
    public async Task Login_WrongPassword_CountsFailure_AndLocksAfterMax()
    {
        var (service, users, _, clock, _) = Create();
        await users.AddAsync(new User { Id = 1, LoginName = "admin", DisplayName = "管理员", PasswordHash = "H:admin123", RoleId = 1, CreatedAtUtc = DateTime.UtcNow });

        for (var i = 0; i < AuthenticationService.MaxFailedAttempts; i++)
        {
            var r = await service.LoginAsync("admin", "wrong");
            Assert.False(r.Success);
        }

        var locked = await users.GetByLoginNameAsync("admin");
        Assert.NotNull(locked!.LockedUntilUtc);

        // 锁定期间即使密码正确也拒绝
        clock.Advance(TimeSpan.FromMinutes(1));
        var attempt = await service.LoginAsync("admin", "admin123");
        Assert.False(attempt.Success);
        Assert.Contains("锁定", attempt.Error);
    }

    [Fact]
    public async Task ChangePassword_ClearsMustChangeFlag()
    {
        var (service, users, _, _, _) = Create();
        await users.AddAsync(new User { Id = 1, LoginName = "admin", DisplayName = "管理员", PasswordHash = "H:admin123", MustChangePassword = true, RoleId = 1, CreatedAtUtc = DateTime.UtcNow });

        await service.ChangePasswordAsync(1, "admin123", "newpassword1");

        var user = await users.GetByIdAsync(1);
        Assert.False(user!.MustChangePassword);
        Assert.Equal("H:newpassword1", user.PasswordHash);
    }

    [Fact]
    public async Task RevokeSession_MakesContextInvalid()
    {
        var (service, users, sessions, _, _) = Create();
        await users.AddAsync(new User { Id = 1, LoginName = "admin", DisplayName = "管理员", PasswordHash = "H:admin123", RoleId = 1, CreatedAtUtc = DateTime.UtcNow });
        var login = await service.LoginAsync("admin", "admin123");

        var context = await service.BuildUserContextAsync(login.Session!.Token);
        Assert.NotNull(context);

        await service.RevokeAsync(login.Session.Token);
        await Assert.ThrowsAsync<Common.AuthorizationException>(() => service.BuildUserContextAsync(login.Session.Token));
    }

    [Fact]
    public async Task ChangePassword_WeakPassword_IsRejected()
    {
        var (service, users, _, _, _) = Create();
        await users.AddAsync(new User { Id = 1, LoginName = "admin", DisplayName = "管理员", PasswordHash = "H:admin123", MustChangePassword = true, RoleId = 1, CreatedAtUtc = DateTime.UtcNow });

        await Assert.ThrowsAsync<DomainException>(() => service.ChangePasswordAsync(1, "admin123", "abcdefgh")); // 无数字
        await Assert.ThrowsAsync<DomainException>(() => service.ChangePasswordAsync(1, "admin123", "12345678")); // 无字母
    }
}