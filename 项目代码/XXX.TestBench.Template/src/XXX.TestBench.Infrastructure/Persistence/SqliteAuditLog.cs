using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 基于数据库的审计日志实现。
/// </summary>
public sealed class SqliteAuditLog : SqliteRepositoryBase, IAuditLog
{
    /// <summary>
    /// 创建审计日志仓库。
    /// </summary>
    public SqliteAuditLog(ISqliteConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 读取最近若干条审计记录。
    /// </summary>
    public Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default)
        => SearchAsync(new AuditLogQuery(Limit: limit), ct);

    /// <summary>
    /// 按条件读取审计记录，结果按时间倒序返回。
    /// </summary>
    public async Task<IReadOnlyList<AuditEntry>> SearchAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        var limit = Math.Clamp(query.Limit, 1, 1000);
        var actor = NullIfWhiteSpace(query.Actor);
        var action = NullIfWhiteSpace(query.Action);
        var text = NullIfWhiteSpace(query.Text);
        var fromUtc = ToUtcText(query.FromUtcInclusive);
        var toUtc = ToUtcText(query.ToUtcExclusive);

        var rows = await RunDbAsync(() =>
        {
            var select = Select<SqliteAuditEntry>();
            if (actor is not null)
                select = select.Where(x => x.Actor.Contains(actor));
            if (action is not null)
                select = select.Where(x => x.Action == action);
            if (text is not null)
                select = select.Where(x => x.Target!.Contains(text) || x.Detail!.Contains(text));
            if (fromUtc is not null)
                select = select.Where(x => x.CreatedAtUtc.CompareTo(fromUtc) >= 0);
            if (toUtc is not null)
                select = select.Where(x => x.CreatedAtUtc.CompareTo(toUtc) < 0);

            return select
                .OrderByDescending(x => x.CreatedAtUtc)
                .OrderByDescending(x => x.Id)
                .Limit(limit)
                .ToList();
        }, ct);

        return rows.Select(row => new AuditEntry(
            row.Id,
            row.Actor,
            row.Action,
            row.Target,
            row.Detail,
            ParseUtc(row.CreatedAtUtc))).ToList();
    }

    /// <summary>
    /// 写入一条审计记录。
    /// </summary>
    public Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default)
        => InsertAsync(new SqliteAuditEntry
        {
            Actor = actor,
            Action = action,
            Target = target,
            Detail = detail,
            CreatedAtUtc = DateTime.UtcNow.ToString("O")
        }, ct);

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ToUtcText(DateTime? value)
    {
        if (!value.HasValue) return null;
        var utc = value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : value.Value.ToUniversalTime();
        return utc.ToString("O");
    }
}
