using System.Data;
using System.Data.Common;
using FreeSql;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 数据库连接工厂接口。
/// </summary>
public interface ISqliteConnectionFactory : IDisposable
{
    /// <summary>
    /// 数据库文件路径。
    /// </summary>
    string DatabasePath { get; }

    /// <summary>
    /// 统一数据访问对象。
    /// </summary>
    IFreeSql Db { get; }

    /// <summary>
    /// 异步租借一个已开启外键与日志模式的连接。
    /// </summary>
    Task<FreeSqlConnectionLease> OpenLeaseAsync(CancellationToken ct = default);

    /// <summary>
    /// 同步租借一个连接，供事务边界使用。
    /// </summary>
    FreeSqlConnectionLease OpenLease();
}

/// <summary>
/// 从连接池租借的连接；仅当显式事务需要跨多次仓储调用时使用。
/// </summary>
public sealed class FreeSqlConnectionLease : IDisposable, IAsyncDisposable
{
    private FreeSql.Internal.ObjectPool.Object<DbConnection>? _lease;

    internal FreeSqlConnectionLease(FreeSql.Internal.ObjectPool.Object<DbConnection> lease)
    {
        _lease = lease;
        Connection = lease.Value;
    }

    /// <summary>
    /// 已租借的数据库连接。
    /// </summary>
    public DbConnection Connection { get; }

    /// <summary>
    /// 归还连接并释放租约。
    /// </summary>
    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;

        Connection.Close();
        lease.Dispose();
    }

    /// <summary>
    /// 异步归还连接并释放租约。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;

        await Connection.CloseAsync();
        lease.Dispose();
    }
}

/// <summary>
/// 数据库连接工厂实现。
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    /// <summary>
    /// 数据库文件完整路径。
    /// </summary>
    private readonly string _databasePath;
    /// <summary>
    /// 统一数据访问对象。
    /// </summary>
    private readonly IFreeSql _db;
    /// <summary>
    /// 释放标记。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 创建连接工厂并初始化数据库访问对象。
    /// </summary>
    public SqliteConnectionFactory(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".");
        _databasePath = Path.GetFullPath(databasePath);

        _db = new FreeSqlBuilder()
            .UseConnectionString(
                DataType.Sqlite,
                $"Data Source={_databasePath};Foreign Keys=True;Pooling=True;")
            .UseAdoConnectionPool(true)
            .UseAutoSyncStructure(false)
            .Build();
    }

    /// <summary>
    /// 数据库文件路径。
    /// </summary>
    public string DatabasePath => _databasePath;

    /// <summary>
    /// 统一数据访问对象。
    /// </summary>
    public IFreeSql Db => _db;

    /// <summary>
    /// 异步租借连接并应用外键与日志模式。
    /// </summary>
    public async Task<FreeSqlConnectionLease> OpenLeaseAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var pool = _db.Ado.MasterPool
            ?? throw new InvalidOperationException("FreeSql SQLite 主连接池未初始化。");
        var lease = pool.Get();
        try
        {
            if (lease.Value.State != ConnectionState.Open)
                await lease.Value.OpenAsync(ct);
            await _db.Ado.ExecuteNonQueryAsync(lease.Value, null, "PRAGMA foreign_keys = ON", new { }, ct);
            await _db.Ado.ExecuteNonQueryAsync(lease.Value, null, "PRAGMA journal_mode = WAL", new { }, ct);
            return new FreeSqlConnectionLease(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 事务边界使用的同步版本；不等待完成，以便调用方能看到事务上下文变化。
    /// </summary>
    public FreeSqlConnectionLease OpenLease()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var pool = _db.Ado.MasterPool
            ?? throw new InvalidOperationException("FreeSql SQLite 主连接池未初始化。");
        var lease = pool.Get();
        try
        {
            if (lease.Value.State != ConnectionState.Open)
                lease.Value.Open();
            _db.Ado.ExecuteNonQuery(lease.Value, null, "PRAGMA foreign_keys = ON", new { });
            _db.Ado.ExecuteNonQuery(lease.Value, null, "PRAGMA journal_mode = WAL", new { });
            return new FreeSqlConnectionLease(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _db.Dispose();
    }
}
