using System.Collections.Concurrent;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Services;

/// <summary>
/// 内存会话管理（进程内）。重启后会话失效，需重新登录；会话表落库在阶段 2 后按需引入。
/// </summary>
public sealed class InMemorySessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

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

    public Task<Session?> GetActiveAsync(string token, CancellationToken ct = default)
    {
        _sessions.TryGetValue(token, out var session);
        return Task.FromResult(session);
    }

    public Task RevokeAsync(string token, CancellationToken ct = default)
    {
        _sessions.TryRemove(token, out _);
        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(int userId, CancellationToken ct = default)
    {
        foreach (var kv in _sessions.Where(kv => kv.Value.UserId == userId))
            _sessions.TryRemove(kv.Key, out _);
        return Task.CompletedTask;
    }
}
