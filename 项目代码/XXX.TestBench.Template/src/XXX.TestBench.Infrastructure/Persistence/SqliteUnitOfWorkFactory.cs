namespace XXX.TestBench.Infrastructure.Persistence;

public sealed class SqliteUnitOfWorkFactory(ISqliteConnectionFactory factory) : Core.Ports.IUnitOfWorkFactory
{
    public Core.Ports.IUnitOfWork Create() => new SqliteUnitOfWork(factory);
}
