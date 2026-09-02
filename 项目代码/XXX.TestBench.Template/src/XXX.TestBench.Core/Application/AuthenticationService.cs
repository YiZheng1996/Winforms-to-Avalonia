using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

public sealed record LoginResult(bool Success, string? Error, Session? Session, User? User);

/// <summary>登录、失败锁定、首次强制改密与会话撤销。</summary>
public sealed class AuthenticationService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionManager _sessions;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public AuthenticationService(IUserRepository users, IPasswordHasher hasher, ISessionManager sessions, IClock clock, IAuditLog audit)
    {
        _users = users;
        _hasher = hasher;
        _sessions = sessions;
        _clock = clock;
        _audit = audit;
    }

    public async Task<LoginResult> LoginAsync(string loginName, string password, CancellationToken ct = default)
    {
        var user = await _users.GetByLoginNameAsync(loginName, ct);
        if (user is null)
        {
            await _audit.WriteAsync("unknown", "LoginFailed", loginName, "用户名不存在", ct);
            return new LoginResult(false, "用户名或密码错误", null, null);
        }

        if (!user.IsEnabled)
        {
            await _audit.WriteAsync(user.LoginName, "LoginBlocked", user.LoginName, "用户已停用", ct);
            return new LoginResult(false, "用户已停用，请联系管理员", null, user);
        }

        if (user.LockedUntilUtc is { } lockedUntil && lockedUntil > _clock.UtcNow)
        {
            await _audit.WriteAsync(user.LoginName, "LoginLocked", user.LoginName, $"锁定至 {lockedUntil:O}", ct);
            return new LoginResult(false, $"登录失败次数过多，已锁定至 {lockedUntil:HH:mm:ss}", null, user);
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount += 1;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntilUtc = _clock.UtcNow + LockDuration;
                user.FailedLoginCount = 0;
            }
            await _users.UpdateAsync(user, ct);
            await _audit.WriteAsync(user.LoginName, "LoginFailed", user.LoginName, $"密码错误，第 {user.FailedLoginCount + (user.LockedUntilUtc is null ? 1 : 0)} 次", ct);
            return new LoginResult(false, "用户名或密码错误", null, user);
        }

        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        await _users.UpdateAsync(user, ct);

        var session = await _sessions.CreateAsync(user, SessionLifetime, ct);
        await _audit.WriteAsync(user.LoginName, "LoginSucceeded", user.LoginName, $"会话 {session.Token[..8]}", ct);
        return new LoginResult(true, null, session, user);
    }

    public async Task ChangePasswordAsync(int userId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new DomainException("用户不存在");
        if (!_hasher.Verify(oldPassword, user.PasswordHash))
            throw new DomainException("原密码错误");
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new DomainException("新密码不能为空");
        if (newPassword == oldPassword)
            throw new DomainException("新密码不能与原密码相同");

        user.PasswordHash = _hasher.Hash(newPassword);
        user.MustChangePassword = false;
        await _users.UpdateAsync(user, ct);
        await _audit.WriteAsync(user.LoginName, "PasswordChanged", user.LoginName, null, ct);
    }

    public async Task<Session?> ValidateSessionAsync(string token, CancellationToken ct = default)
    {
        var session = await _sessions.GetActiveAsync(token, ct);
        if (session is null) return null;
        return session.IsActiveAt(_clock.UtcNow) ? session : null;
    }

    public async Task RevokeAsync(string token, CancellationToken ct = default)
    {
        await _sessions.RevokeAsync(token, ct);
        await _audit.WriteAsync("session", "SessionRevoked", token[..8], null, ct);
    }

    public async Task<UserContext> BuildUserContextAsync(string token, CancellationToken ct = default)
    {
        var session = await ValidateSessionAsync(token, ct) ?? throw new AuthorizationException("会话无效或已过期");
        var user = await _users.GetByIdAsync(session.UserId, ct) ?? throw new AuthorizationException("用户不存在");
        var role = await _users.GetRoleAsync(user.RoleId, ct) ?? throw new AuthorizationException("角色不存在");
        return new UserContext { UserId = user.Id, LoginName = user.LoginName, DisplayName = user.DisplayName, Role = role };
    }
}
