using FreeSql;
using XXX.TestBench.Infrastructure.Persistence;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// Shared FreeSql ADO boundary for SQLite repositories. SQL remains explicit
/// because the existing database schema and migration history are preserved;
/// no provider-specific driver types cross this boundary.
/// </summary>
public abstract class SqliteRepositoryBase
{
    private readonly ISqliteConnectionFactory _factory;

    protected SqliteRepositoryBase(ISqliteConnectionFactory factory) => _factory = factory;

    protected IFreeSql Db => _factory.Db;

    protected Task<List<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken ct = default)
    {
        var values = parameters ?? new { };
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? Db.Ado.QueryAsync<T>(sql, values, ct)
            : Db.Ado.QueryAsync<T>(ambient.Connection, ambient.Transaction, sql, values, ct);
    }

    protected async Task<T?> QuerySingleAsync<T>(string sql, object? parameters, CancellationToken ct = default)
    {
        var rows = await QueryAsync<T>(sql, parameters, ct);
        return rows.FirstOrDefault();
    }

    protected Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        var values = parameters ?? new { };
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? Db.Ado.ExecuteNonQueryAsync(sql, values, ct)
            : Db.Ado.ExecuteNonQueryAsync(ambient.Connection, ambient.Transaction, sql, values, ct);
    }

    protected Task<object> ScalarAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        var values = parameters ?? new { };
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? Db.Ado.ExecuteScalarAsync(sql, values, ct)
            : Db.Ado.ExecuteScalarAsync(ambient.Connection, ambient.Transaction, sql, values, ct);
    }

    protected async Task<long> LastInsertRowIdAsync(CancellationToken ct = default)
    {
        var value = await ScalarAsync("SELECT last_insert_rowid()", null, ct);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Executes an INSERT and reads last_insert_rowid on the same physical
    /// connection. SQLite keeps that value per connection, so this is required
    /// when FreeSql is using its automatic connection pool.
    /// </summary>
    protected async Task<long> ExecuteInsertAndGetIdAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        var values = parameters ?? new { };
        var ambient = SqliteAmbient.Current;
        if (ambient is not null)
        {
            await Db.Ado.ExecuteNonQueryAsync(ambient.Connection, ambient.Transaction, sql, values, ct);
            var value = await Db.Ado.ExecuteScalarAsync(ambient.Connection, ambient.Transaction, "SELECT last_insert_rowid()", new { }, ct);
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        await using var lease = await _factory.OpenLeaseAsync(ct);
        await Db.Ado.ExecuteNonQueryAsync(lease.Connection, null, sql, values, ct);
        var id = await Db.Ado.ExecuteScalarAsync(lease.Connection, null, "SELECT last_insert_rowid()", new { }, ct);
        return Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture);
    }

    protected static object DbValue(object? value) => value ?? DBNull.Value;

    protected static string? ParseNullableString(object? value) => value is DBNull or null ? null : Convert.ToString(value);

    protected static DateTime ParseUtc(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    protected static DateTime? ParseNullableUtc(object? value) => value is DBNull or null ? null : ParseUtc(Convert.ToString(value)!);
}
