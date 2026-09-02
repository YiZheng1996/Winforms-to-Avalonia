using System.Data.Common;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// Ambient FreeSql transaction context. Repositories use its connection and
/// transaction through FreeSql.Ado when a business operation is atomic.
/// </summary>
public sealed class FreeSqlTransactionContext : IDisposable, IAsyncDisposable
{
    private FreeSqlConnectionLease? _lease;

    internal FreeSqlTransactionContext(FreeSqlConnectionLease lease, DbTransaction transaction)
    {
        _lease = lease;
        Connection = lease.Connection;
        Transaction = transaction;
    }

    public DbConnection Connection { get; }

    public DbTransaction Transaction { get; }

    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;

        Transaction.Dispose();
        lease.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;

        await Transaction.DisposeAsync();
        await lease.DisposeAsync();
    }
}

/// <summary>
/// Current transaction for the async call context. The name is kept for
/// compatibility with the existing SQLite persistence boundary.
/// </summary>
public static class SqliteAmbient
{
    private static readonly AsyncLocal<FreeSqlTransactionContext?> _current = new();

    public static FreeSqlTransactionContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly ISqliteConnectionFactory _factory;
    private FreeSqlConnectionLease? _lease;
    private FreeSqlTransactionContext? _context;
    private bool _completed;

    public SqliteUnitOfWork(ISqliteConnectionFactory factory) => _factory = factory;

    public Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_context is not null) return Task.CompletedTask;

        _lease = _factory.OpenLease();
        try
        {
            var transaction = _lease!.Connection.BeginTransaction();
            _context = new FreeSqlTransactionContext(_lease, transaction);
            _lease = null;
            SqliteAmbient.Current = _context;
            return Task.CompletedTask;
        }
        catch
        {
            _lease!.Dispose();
            _lease = null;
            throw;
        }
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_context is null || _completed) return;
        await _context.Transaction.CommitAsync(ct);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_context is null || _completed) return;
        await _context.Transaction.RollbackAsync(ct);
        _completed = true;
    }

    public ValueTask DisposeAsync()
    {
        if (_context is null)
        {
            var lease = _lease;
            _lease = null;
            return lease?.DisposeAsync() ?? default;
        }

        var context = _context;
        _context = null;
        if (ReferenceEquals(SqliteAmbient.Current, context))
            SqliteAmbient.Current = null;

        if (!_completed)
            return DisposeAfterRollbackAsync(context);

        return context.DisposeAsync();
    }

    private static async ValueTask DisposeAfterRollbackAsync(FreeSqlTransactionContext context)
    {
        try { await context.Transaction.RollbackAsync(); }
        catch { /* disposal must still release the FreeSql pool lease */ }
        await context.DisposeAsync();
    }
}
