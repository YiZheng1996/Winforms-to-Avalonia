using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence.Repositories;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 基于数据库的审计日志实现。
/// </summary>
public sealed class SqliteAuditLog(ISqliteConnectionFactory factory) : IAuditLog
{
    /// <summary>
    /// 读取最近若干条审计记录。
    /// </summary>
    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default)
    {
        var rows = await QueryAsync<AuditRow>(
            "SELECT id AS Id, actor AS Actor, action AS Action, target AS Target, detail AS Detail, created_at_utc AS CreatedAtUtc FROM audit_logs ORDER BY created_at_utc DESC, id DESC LIMIT @limit",
            new { limit }, ct);
        return rows.Select(row => new AuditEntry(
            row.Id,
            row.Actor,
            row.Action,
            row.Target,
            row.Detail,
            DateTime.Parse(row.CreatedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind))).ToList();
    }

    /// <summary>
    /// 写入一条审计记录。
    /// </summary>
    public Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default) => ExecuteAsync("""
        INSERT INTO audit_logs (actor, action, target, detail, created_at_utc)
        VALUES (@actor, @action, @target, @detail, @createdAtUtc)
        """, new
    {
        actor,
        action,
        target = target ?? (object)DBNull.Value,
        detail = detail ?? (object)DBNull.Value,
        createdAtUtc = DateTime.UtcNow.ToString("O")
    }, ct);

    /// <summary>
    /// 查询辅助，存在当前事务时优先使用事务连接。
    /// </summary>
    private Task<List<T>> QueryAsync<T>(string sql, object parameters, CancellationToken ct)
    {
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? factory.Db.Ado.QueryAsync<T>(sql, parameters, ct)
            : factory.Db.Ado.QueryAsync<T>(ambient.Connection, ambient.Transaction, sql, parameters, ct);
    }

    /// <summary>
    /// 执行辅助，存在当前事务时优先使用事务连接。
    /// </summary>
    private Task<int> ExecuteAsync(string sql, object parameters, CancellationToken ct)
    {
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? factory.Db.Ado.ExecuteNonQueryAsync(sql, parameters, ct)
            : factory.Db.Ado.ExecuteNonQueryAsync(ambient.Connection, ambient.Transaction, sql, parameters, ct);
    }

    private sealed class AuditRow
    {
        public int Id { get; set; }
        public string Actor { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? Target { get; set; }
        public string? Detail { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
    }
}
