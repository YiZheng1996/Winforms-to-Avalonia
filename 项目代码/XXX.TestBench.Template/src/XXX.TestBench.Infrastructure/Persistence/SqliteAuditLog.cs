using Microsoft.Data.Sqlite;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence;

public sealed class SqliteAuditLog : IAuditLog
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteAuditLog(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default)
    {
        var conn = SqliteAmbient.Current ?? _factory.Open();
        var owns = SqliteAmbient.Current is null;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO audit_logs (actor, action, target, detail, created_at_utc)
                VALUES ($actor, $action, $target, $detail, $now)
                """;
            cmd.Parameters.AddWithValue("$actor", actor);
            cmd.Parameters.AddWithValue("$action", action);
            cmd.Parameters.AddWithValue("$target", (object?)target ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (owns) await conn.DisposeAsync();
        }
    }
}
