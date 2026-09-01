namespace XXX.TestBench.Infrastructure.Persistence;

public sealed class SqliteUnitOfWorkFactory : Core.Ports.IUnitOfWorkFactory
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteUnitOfWorkFactory(ISqliteConnectionFactory factory) => _factory = factory;

    public Core.Ports.IUnitOfWork Create() => new SqliteUnitOfWork(_factory);
}
