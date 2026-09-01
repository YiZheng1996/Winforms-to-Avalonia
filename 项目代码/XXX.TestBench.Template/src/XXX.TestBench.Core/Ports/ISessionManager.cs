using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.Core.Ports;

public interface ISessionManager
{
    Task<Session> CreateAsync(User user, TimeSpan lifetime, CancellationToken ct = default);
    Task<Session?> GetActiveAsync(string token, CancellationToken ct = default);
    Task RevokeAsync(string token, CancellationToken ct = default);
    Task RevokeAllForUserAsync(int userId, CancellationToken ct = default);
}
