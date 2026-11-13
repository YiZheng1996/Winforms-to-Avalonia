using System.Collections.Concurrent;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Services;

/// <summary>
/// 内存会话管理（进程内）。重启后会话失效，需重新登录；会话表落库在阶段 2 后按需引入。
/// </summary>
public sealed class InMemorySessionManager : ISessionManager
{
    /// <summary>
    /// 会话字典，键为会话令牌。
    /// </summary>
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    /// <summary>
    /// 为用户创建并保存一个会话。
    /// </summary>
    public Task<Session> CreateAsync(User user, TimeSpan lifetime, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var session = new Session
        {
            Token = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + lifetime
        };
        _sessions[session.Token] = session;
        return Task.FromResult(session);
    }

    /// <summary>
    /// 按令牌读取会话，由调用方判断是否有效。
    /// </summary>
    public Task<Session?> GetActiveAsync(string token, CancellationToken ct = default)
    {
        _sessions.TryGetValue(token, out var session);
        return Task.FromResult(session);
    }

    /// <summary>
    /// 撤销指定会话。
    /// </summary>
    public Task RevokeAsync(string token, CancellationToken ct = default)
    {
        _sessions.TryRemove(token, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 撤销某用户的全部会话。
    /// </summary>
    public Task RevokeAllForUserAsync(int userId, CancellationToken ct = default)
    {
        foreach (var kv in _sessions.Where(kv => kv.Value.UserId == userId))
            _sessions.TryRemove(kv.Key, out _);
        return Task.CompletedTask;
    }
}
