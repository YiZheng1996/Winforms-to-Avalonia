using System.Data.Common;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 事务上下文：保存租借连接与事务对象；仓储在需要原子操作时使用它。
/// </summary>
public sealed class FreeSqlTransactionContext : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 已租借的连接。
    /// </summary>
    private FreeSqlConnectionLease? _lease;

    internal FreeSqlTransactionContext(FreeSqlConnectionLease lease, DbTransaction transaction)
    {
        _lease = lease;
        Connection = lease.Connection;
        Transaction = transaction;
    }

    /// <summary>
    /// 事务使用的数据库连接。
    /// </summary>
    public DbConnection Connection { get; }

    /// <summary>
    /// 当前事务对象。
    /// </summary>
    public DbTransaction Transaction { get; }

    /// <summary>
    /// 释放事务上下文。
    /// </summary>
    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;

        Transaction.Dispose();
        lease.Dispose();
    }

    /// <summary>
    /// 异步释放事务上下文。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;

        await Transaction.DisposeAsync();
        await lease.DisposeAsync();
    }
}

/// <summary>
/// 保存当前异步流程的事务上下文。
/// </summary>
public static class SqliteAmbient
{
    private static readonly AsyncLocal<FreeSqlTransactionContext?> _current = new();

    /// <summary>
    /// 当前事务上下文。
    /// </summary>
    public static FreeSqlTransactionContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

/// <summary>
/// 数据库工作单元实现，负责事务开启、提交与回滚。
/// </summary>
public sealed class SqliteUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// 数据库连接工厂。
    /// </summary>
    private readonly ISqliteConnectionFactory _factory;
    private FreeSqlConnectionLease? _lease;
    /// <summary>
    /// 当前事务上下文。
    /// </summary>
    private FreeSqlTransactionContext? _context;
    /// <summary>
    /// 是否已完成标记。
    /// </summary>
    private bool _completed;

    /// <summary>
    /// 创建工作单元。
    /// </summary>
    public SqliteUnitOfWork(ISqliteConnectionFactory factory) => _factory = factory;

    /// <summary>
    /// 开启事务并写入当前异步上下文。
    /// </summary>
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

    /// <summary>
    /// 提交事务。
    /// </summary>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_context is null || _completed) return;
        await _context.Transaction.CommitAsync(ct);
        _completed = true;
    }

    /// <summary>
    /// 回滚事务。
    /// </summary>
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_context is null || _completed) return;
        await _context.Transaction.RollbackAsync(ct);
        _completed = true;
    }

    /// <summary>
    /// 释放连接与事务资源。
    /// </summary>
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

    /// <summary>
    /// 回滚后释放事务上下文。
    /// </summary>
    private static async ValueTask DisposeAfterRollbackAsync(FreeSqlTransactionContext context)
    {
        try { await context.Transaction.RollbackAsync(); }
        catch { /* disposal must still release the FreeSql pool lease */ }
        await context.DisposeAsync();
    }
}
