namespace XXX.TestBench.Core.Ports;

public interface IAuditLog
{
    Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default);
}
