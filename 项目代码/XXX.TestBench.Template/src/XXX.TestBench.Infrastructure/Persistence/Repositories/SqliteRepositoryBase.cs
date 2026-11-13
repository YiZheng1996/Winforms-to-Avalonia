using System.Globalization;
using System.Data.Common;
using FreeSql;
using XXX.TestBench.Infrastructure.Persistence;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 各仓储共用的 FreeSql Lambda 访问基类。
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
    /// 创建带当前事务连接的 Lambda 查询。
    /// </summary>
    protected ISelect<T> Select<T>() where T : class, new()
    {
        var query = Db.Select<T>();
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? query
            : query.WithConnection(ambient.Connection).WithTransaction(ambient.Transaction);
    }

    /// <summary>
    /// 创建绑定到显式事务的 Lambda 查询。
    /// </summary>
    protected ISelect<T> Select<T>(DbConnection connection, DbTransaction transaction) where T : class, new()
        => Db.Select<T>().WithConnection(connection).WithTransaction(transaction);

    /// <summary>
    /// 创建带当前事务连接的 Lambda 新增操作。
    /// </summary>
    protected IInsert<T> Insert<T>(T source) where T : class, new()
    {
        var insert = Db.Insert(source);
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? insert
            : insert.WithConnection(ambient.Connection).WithTransaction(ambient.Transaction);
    }

    /// <summary>
    /// 创建绑定到显式事务的 Lambda 新增操作。
    /// </summary>
    protected IInsert<T> Insert<T>(T source, DbConnection connection, DbTransaction transaction) where T : class, new()
        => Db.Insert(source).WithConnection(connection).WithTransaction(transaction);

    /// <summary>
    /// 创建带当前事务连接的批量新增操作。
    /// </summary>
    protected IInsert<T> InsertMany<T>(IEnumerable<T> source) where T : class, new()
    {
        var insert = Db.Insert(source);
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? insert
            : insert.WithConnection(ambient.Connection).WithTransaction(ambient.Transaction);
    }

    /// <summary>
    /// 创建绑定到显式事务的批量新增操作。
    /// </summary>
    protected IInsert<T> InsertMany<T>(IEnumerable<T> source, DbConnection connection, DbTransaction transaction) where T : class, new()
        => Db.Insert(source).WithConnection(connection).WithTransaction(transaction);

    /// <summary>
    /// 创建带当前事务连接的插入或更新操作。
    /// </summary>
    protected IInsertOrUpdate<T> InsertOrUpdate<T>() where T : class, new()
    {
        var upsert = Db.InsertOrUpdate<T>();
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? upsert
            : upsert.WithConnection(ambient.Connection).WithTransaction(ambient.Transaction);
    }

    /// <summary>
    /// 创建绑定到显式事务的插入或更新操作。
    /// </summary>
    protected IInsertOrUpdate<T> InsertOrUpdate<T>(DbConnection connection, DbTransaction transaction) where T : class, new()
        => Db.InsertOrUpdate<T>().WithConnection(connection).WithTransaction(transaction);

    /// <summary>
    /// 创建带当前事务连接的 Lambda 更新操作。
    /// </summary>
    protected IUpdate<T> Update<T>() where T : class, new()
    {
        var update = Db.Update<T>();
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? update
            : update.WithConnection(ambient.Connection).WithTransaction(ambient.Transaction);
    }

    /// <summary>
    /// 创建绑定到显式事务的 Lambda 更新操作。
    /// </summary>
    protected IUpdate<T> Update<T>(DbConnection connection, DbTransaction transaction) where T : class, new()
        => Db.Update<T>().WithConnection(connection).WithTransaction(transaction);

    /// <summary>
    /// 创建带当前事务连接的 Lambda 删除操作。
    /// </summary>
    protected IDelete<T> Delete<T>() where T : class, new()
    {
        var delete = Db.Delete<T>();
        var ambient = SqliteAmbient.Current;
        return ambient is null
            ? delete
            : delete.WithConnection(ambient.Connection).WithTransaction(ambient.Transaction);
    }

    /// <summary>
    /// 创建绑定到显式事务的 Lambda 删除操作。
    /// </summary>
    protected IDelete<T> Delete<T>(DbConnection connection, DbTransaction transaction) where T : class, new()
        => Db.Delete<T>().WithConnection(connection).WithTransaction(transaction);

    /// <summary>
    /// 执行 FreeSql 的同步 fluent API，并保持仓储接口的异步形态。
    /// FreeSql 3.5 的 Lambda 执行器本身没有异步 ToList/Execute 方法；这里同步完成操作，
    /// 使仓储方法仍可实现现有异步契约，同时避免多个 UI 加载回调交错修改集合。
    /// </summary>
    protected static Task<TResult> RunDbAsync<TResult>(Func<TResult> operation, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(operation());
    }

    /// <summary>
    /// 执行无返回值的 FreeSql fluent API。
    /// </summary>
    protected static Task RunDbAsync(Action operation, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        operation();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行新增并返回数据库生成的自增编号。
    /// </summary>
    protected Task<long> InsertIdentityAsync<T>(T source, CancellationToken ct = default) where T : class, new()
        => RunDbAsync(() => Insert(source).ExecuteIdentity(), ct);

    /// <summary>
    /// 执行新增并返回受影响行数。
    /// </summary>
    protected Task<int> InsertAsync<T>(T source, CancellationToken ct = default) where T : class, new()
        => RunDbAsync(() => Insert(source).ExecuteAffrows(), ct);

    /// <summary>
    /// 按往返格式解析时间。
    /// </summary>
    protected static DateTime ParseUtc(string value) => DateTime.Parse(value, null, DateTimeStyles.RoundtripKind);

    /// <summary>
    /// 解析可空时间，空值保持为空。
    /// </summary>
    protected static DateTime? ParseNullableUtc(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : ParseUtc(value);
}
