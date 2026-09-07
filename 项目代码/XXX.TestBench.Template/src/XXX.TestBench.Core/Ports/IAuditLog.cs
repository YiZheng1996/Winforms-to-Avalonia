namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 审计记录项，记录操作者、动作与时间。
/// </summary>
public sealed record AuditEntry(int Id, string Actor, string Action, string? Target, string? Detail, DateTime CreatedAtUtc);

/// <summary>
/// 审计日志查询条件。时间边界统一使用 UTC，结束时间为开区间。
/// </summary>
public sealed record AuditLogQuery(
    string? Actor = null,
    string? Action = null,
    string? Text = null,
    DateTime? FromUtcInclusive = null,
    DateTime? ToUtcExclusive = null,
    int Limit = 200);

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
    /// <summary>
    /// 按操作者、动作、对象/详情关键字和时间范围查询审计记录。
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> SearchAsync(AuditLogQuery query, CancellationToken ct = default);
}
