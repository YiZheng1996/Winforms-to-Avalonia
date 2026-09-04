using FreeSql;
using XXX.TestBench.Infrastructure.Persistence;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 各仓储共用的数据库访问基类；保留手写语句以兼容现有表结构与迁移历史。
/// </summary>
public abstract class SqliteRepositoryBase
{
    /// <summary>
    /// 数据库连接工厂。
    /// </summary>
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>
    /// 创建仓储基类。
    /// </summary>
    protected SqliteRepositoryBase(ISqliteConnectionFactory factory) => _factory = factory;

    /// <summary>
    /// 统一数据访问对象。
    /// </summary>
    protected IFreeSql Db => _factory.Db;

    /// <summary>
    /// 查询多行，存在当前事务时使用事务连接。
    /// </summary>
    protected Task<List<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken ct = default)
    {
        var values = parameters ?? new { };
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? Db.Ado.QueryAsync<T>(sql, values, ct)
            : Db.Ado.QueryAsync<T>(ambient.Connection, ambient.Transaction, sql, values, ct);
    }

    /// <summary>
    /// 查询单行，无结果时返回空。
    /// </summary>
    protected async Task<T?> QuerySingleAsync<T>(string sql, object? parameters, CancellationToken ct = default)
    {
        var rows = await QueryAsync<T>(sql, parameters, ct);
        return rows.FirstOrDefault();
    }

    /// <summary>
    /// 执行写入语句，返回受影响行数。
    /// </summary>
    protected Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        var values = parameters ?? new { };
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? Db.Ado.ExecuteNonQueryAsync(sql, values, ct)
            : Db.Ado.ExecuteNonQueryAsync(ambient.Connection, ambient.Transaction, sql, values, ct);
    }

    /// <summary>
    /// 执行查询并返回首行首列值。
    /// </summary>
    protected Task<object> ScalarAsync(string sql, object? parameters, CancellationToken ct = default)
    {
        var values = parameters ?? new { };
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? Db.Ado.ExecuteScalarAsync(sql, values, ct)
            : Db.Ado.ExecuteScalarAsync(ambient.Connection, ambient.Transaction, sql, values, ct);
    }

    /// <summary>
    /// 读取当前连接最后插入的自增编号。
    /// </summary>
    protected async Task<long> LastInsertRowIdAsync(CancellationToken ct = default)
    {
        var value = await ScalarAsync("SELECT last_insert_rowid()", null, ct);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 在同一物理连接上执行新增并读取自增编号。
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

    /// <summary>
    /// 空值转为数据库空值。
    /// </summary>
    protected static object DbValue(object? value) => value ?? DBNull.Value;

    /// <summary>
    /// 把可空字段转换为字符串，空值保持为空。
    /// </summary>
    protected static string? ParseNullableString(object? value) => value is DBNull or null ? null : Convert.ToString(value);

    /// <summary>
    /// 按往返格式解析时间。
    /// </summary>
    protected static DateTime ParseUtc(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    /// <summary>
    /// 解析可空时间，空值保持为空。
    /// </summary>
    protected static DateTime? ParseNullableUtc(object? value) => value is DBNull or null ? null : ParseUtc(Convert.ToString(value)!);
}
