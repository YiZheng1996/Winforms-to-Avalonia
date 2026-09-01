using Microsoft.Data.Sqlite;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence;

public sealed class SqliteAuditLog : IAuditLog
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteAuditLog(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default)
    {
        var result = new List<AuditEntry>();
        var conn = SqliteAmbient.Current ?? _factory.Open();
        var owns = SqliteAmbient.Current is null;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, actor, action, target, detail, created_at_utc FROM audit_logs ORDER BY id DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(new AuditEntry(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                    DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)));
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

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
