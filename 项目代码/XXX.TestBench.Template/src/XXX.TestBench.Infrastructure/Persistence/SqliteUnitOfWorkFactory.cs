namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 数据库工作单元工厂。
/// </summary>
public sealed class SqliteUnitOfWorkFactory(ISqliteConnectionFactory factory) : Core.Ports.IUnitOfWorkFactory
{
    /// <summary>
    /// 新建一个数据库工作单元。
    /// </summary>
    public Core.Ports.IUnitOfWork Create() => new SqliteUnitOfWork(factory);
}
