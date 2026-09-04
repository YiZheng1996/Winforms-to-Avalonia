using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 登录会话管理接口。
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// 为用户创建一个新会话。
    /// </summary>
    Task<Session> CreateAsync(User user, TimeSpan lifetime, CancellationToken ct = default);
    /// <summary>
    /// 按令牌读取仍有效的会话。
    /// </summary>
    Task<Session?> GetActiveAsync(string token, CancellationToken ct = default);
    /// <summary>
    /// 撤销指定会话。
    /// </summary>
    Task RevokeAsync(string token, CancellationToken ct = default);
    /// <summary>
    /// 撤销某用户的全部会话。
    /// </summary>
    Task RevokeAllForUserAsync(int userId, CancellationToken ct = default);
}
