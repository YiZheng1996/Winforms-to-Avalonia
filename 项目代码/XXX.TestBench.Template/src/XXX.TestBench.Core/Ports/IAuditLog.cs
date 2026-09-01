namespace XXX.TestBench.Core.Ports;

public sealed record AuditEntry(int Id, string Actor, string Action, string? Target, string? Detail, DateTime CreatedAtUtc);

public interface IAuditLog
{
    Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default);
}
