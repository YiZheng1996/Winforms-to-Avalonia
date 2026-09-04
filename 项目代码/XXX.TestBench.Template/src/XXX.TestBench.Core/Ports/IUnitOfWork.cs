namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 工作单元接口，提供数据库事务控制能力。
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// 开启事务。
    /// </summary>
    Task BeginTransactionAsync(CancellationToken ct = default);
    /// <summary>
    /// 提交事务。
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);
    /// <summary>
    /// 回滚事务。
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}
