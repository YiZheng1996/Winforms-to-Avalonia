using System.Data;
using System.Data.Common;
using FreeSql;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// FreeSql-backed SQLite connection factory.
/// The database remains SQLite, while all application data access is exposed
/// through the singleton <see cref="IFreeSql"/> instance.
/// </summary>
public interface ISqliteConnectionFactory : IDisposable
{
    string DatabasePath { get; }

    IFreeSql Db { get; }

    Task<FreeSqlConnectionLease> OpenLeaseAsync(CancellationToken ct = default);

    FreeSqlConnectionLease OpenLease();
}

/// <summary>
/// A leased connection from FreeSql's connection pool. It is used only when
/// an explicit transaction must span several repository calls.
/// </summary>
public sealed class FreeSqlConnectionLease : IDisposable, IAsyncDisposable
{
    private FreeSql.Internal.ObjectPool.Object<DbConnection>? _lease;

    internal FreeSqlConnectionLease(FreeSql.Internal.ObjectPool.Object<DbConnection> lease)
    {
        _lease = lease;
        Connection = lease.Value;
    }

    public DbConnection Connection { get; }

    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;

        Connection.Close();
        lease.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;

        await Connection.CloseAsync();
        lease.Dispose();
    }
}

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _databasePath;
    private readonly IFreeSql _db;
    private int _disposed;

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

    public string DatabasePath => _databasePath;

    public IFreeSql Db => _db;

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
    /// Synchronous counterpart used by the ambient transaction boundary. It
    /// deliberately completes without an await so AsyncLocal changes made by
    /// the unit of work are visible to the caller after BeginTransactionAsync.
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
