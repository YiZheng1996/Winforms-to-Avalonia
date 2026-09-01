using Microsoft.Data.Sqlite;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>当前作用域连接的 Ambient 载体：仓储在存在活动工作单元时复用其连接，保证多语句原子性。</summary>
public static class SqliteAmbient
{
    private static readonly AsyncLocal<SqliteConnection?> _current = new();
    public static SqliteConnection? Current { get => _current.Value; set => _current.Value = value; }
}

public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly ISqliteConnectionFactory _factory;
    private SqliteConnection? _connection;
    private SqliteTransaction? _transaction;

    public SqliteUnitOfWork(ISqliteConnectionFactory factory) => _factory = factory;

    public Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _connection = _factory.Open();
        SqliteAmbient.Current = _connection;
        _transaction = _connection.BeginTransaction();
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        _transaction?.Commit();
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        _transaction?.Rollback();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _transaction?.Dispose();
        _connection?.Dispose();
        SqliteAmbient.Current = null;
        return ValueTask.CompletedTask;
    }
}
