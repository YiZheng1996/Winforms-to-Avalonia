namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 审计记录项，记录操作者、动作与时间。
/// </summary>
public sealed record AuditEntry(int Id, string Actor, string Action, string? Target, string? Detail, DateTime CreatedAtUtc);

/// <summary>
/// 审计日志接口，用于记录和查询操作痕迹。
/// </summary>
public interface IAuditLog
{
    /// <summary>
    /// 写入一条审计记录。
    /// </summary>
    Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default);
    /// <summary>
    /// 读取最近若干条审计记录。
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default);
}
