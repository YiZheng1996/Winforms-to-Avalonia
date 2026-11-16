using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 同一数据库实例创建的一组仓储、事务工厂、结构检查与生命周期对象。
/// </summary>
public sealed class DatabaseServices : IDisposable, IAsyncDisposable
{
    private int _disposed;

    internal DatabaseServices(
        ISqliteConnectionFactory connectionFactory,
        SqliteDatabase database,
        string databaseInstanceId,
        string storageId,
        IUserRepository users,
        IProductRepository products,
        IRecordRepository records,
        ITestPointRepository testPoints,
        IModelPointConfigRepository modelPointConfigs,
        ITestParameterRepository testParameters,
        IReportRepository reports,
        IBenchControlStateStore benchControlStates,
        IAuditLog auditLog,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        ConnectionFactory = connectionFactory;
        Database = database;
        DatabaseInstanceId = databaseInstanceId;
        StorageId = storageId;
        Users = users;
        Products = products;
        Records = records;
        TestPoints = testPoints;
        ModelPointConfigs = modelPointConfigs;
        TestParameters = testParameters;
        Reports = reports;
        BenchControlStates = benchControlStates;
        AuditLog = auditLog;
        UnitOfWorkFactory = unitOfWorkFactory;
    }

    public ISqliteConnectionFactory ConnectionFactory { get; }
    public SqliteDatabase Database { get; }
    public string DatabasePath => ConnectionFactory.DatabasePath;
    public string DatabaseInstanceId { get; }
    public string StorageId { get; }
    public IUserRepository Users { get; }
    public IProductRepository Products { get; }
    public IRecordRepository Records { get; }
    public ITestPointRepository TestPoints { get; }
    public IModelPointConfigRepository ModelPointConfigs { get; }
    public ITestParameterRepository TestParameters { get; }
    public IReportRepository Reports { get; }
    public IBenchControlStateStore BenchControlStates { get; }
    public IAuditLog AuditLog { get; }
    public IUnitOfWorkFactory UnitOfWorkFactory { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ConnectionFactory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
