using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 登录结果，包含是否成功、提示信息、会话与用户。
/// </summary>
public sealed record LoginResult(bool Success, string? Error, Session? Session, User? User);

/// <summary>
/// 登录、失败锁定、首次强制改密与会话撤销。
/// </summary>
public sealed class AuthenticationService
{
    /// <summary>
    /// 最大连续失败次数。
    /// </summary>
    public const int MaxFailedAttempts = 5;
    /// <summary>
    /// 失败锁定时长。
    /// </summary>
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    /// <summary>
    /// 登录会话有效期。
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionManager _sessions;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    /// <summary>
    /// 创建登录服务。
    /// </summary>
    public AuthenticationService(IUserRepository users, IPasswordHasher hasher, ISessionManager sessions, IClock clock, IAuditLog audit)
    {
        _users = users;
        _hasher = hasher;
        _sessions = sessions;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>
    /// 登录：校验账号状态与密码，成功后创建会话。
    /// </summary>
    public async Task<LoginResult> LoginAsync(string loginName, string password, CancellationToken ct = default)
    {
        // 先按登录名找用户，找不到时也按统一提示返回。
        var user = await _users.GetByLoginNameAsync(loginName, ct);
        if (user is null)
        {
            await _audit.WriteAsync("unknown", "LoginFailed", loginName, "用户名不存在", ct);
            return new LoginResult(false, "用户名或密码错误", null, null);
        }

        // 停用账号直接拒绝登录。
        if (!user.IsEnabled)
        {
            await _audit.WriteAsync(user.LoginName, "LoginBlocked", user.LoginName, "用户已停用", ct);
            return new LoginResult(false, "用户已停用，请联系管理员", null, user);
        }

        // 锁定期间直接拒绝登录。
        if (user.LockedUntilUtc is { } lockedUntil && lockedUntil > _clock.UtcNow)
        {
            await _audit.WriteAsync(user.LoginName, "LoginLocked", user.LoginName, $"锁定至 {lockedUntil:O}", ct);
            return new LoginResult(false, $"登录失败次数过多，已锁定至 {lockedUntil:HH:mm:ss}", null, user);
        }

        // 密码错误时累计失败次数，达到上限则临时锁定。
        if (!_hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount += 1;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntilUtc = _clock.UtcNow + LockDuration;
                // 登录成功后清除失败计数与锁定状态。
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

    /// <summary>
    /// 修改密码，成功后清除强制改密标记。
    /// </summary>
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

    /// <summary>
    /// 校验会话是否有效。
    /// </summary>
    public async Task<Session?> ValidateSessionAsync(string token, CancellationToken ct = default)
    {
        var session = await _sessions.GetActiveAsync(token, ct);
        if (session is null) return null;
        return session.IsActiveAt(_clock.UtcNow) ? session : null;
    }

    /// <summary>
    /// 撤销指定会话。
    /// </summary>
    public async Task RevokeAsync(string token, CancellationToken ct = default)
    {
        await _sessions.RevokeAsync(token, ct);
        await _audit.WriteAsync("session", "SessionRevoked", token[..8], null, ct);
    }

    /// <summary>
    /// 依据有效会话构建带权限的操作者上下文。
    /// </summary>
    public async Task<UserContext> BuildUserContextAsync(string token, CancellationToken ct = default)
    {
        var session = await ValidateSessionAsync(token, ct) ?? throw new AuthorizationException("会话无效或已过期");
        var user = await _users.GetByIdAsync(session.UserId, ct) ?? throw new AuthorizationException("用户不存在");
        var role = await _users.GetRoleAsync(user.RoleId, ct) ?? throw new AuthorizationException("角色不存在");
        return new UserContext { UserId = user.Id, LoginName = user.LoginName, DisplayName = user.DisplayName, Role = role };
    }
}
