using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Reports;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 本地 SQLite Provider。启动测试只读；运行时只打开已存在且身份匹配的 v9 数据库。
/// </summary>
public sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    private static readonly IReadOnlySet<string> ProviderCapabilities = new HashSet<string>(StringComparer.Ordinal)
    {
        "read-only-test", "open-existing", "create-new"
    };

    private readonly IPasswordHasher _passwordHasher;

    public SqliteDatabaseProvider(IPasswordHasher passwordHasher)
        => _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));

    public string Key => "sqlite";
    public string DisplayName => "本地 SQLite";
    public IReadOnlySet<string> Capabilities => ProviderCapabilities;

    /// <summary>
    /// 只读检查文件、schema、数据库身份与存储身份；不会创建目录、文件或修改 PRAGMA。
    /// </summary>
    public async Task<DatabaseProviderTestResult> TestAsync(
        DatabaseProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = GetPath(profile);
            if (!File.Exists(path))
                return Failure("目标数据库文件不存在；启动检查不会新建空库。", null);

            var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(path, cancellationToken);
            var issues = SqliteSchemaV9.Validate(inspection).ToList();
            AddIdentityIssue(issues, "DatabaseInstanceId", profile.ExpectedDatabaseInstanceId,
                inspection.AppMetadataDatabaseInstanceId);
            AddIdentityIssue(issues, "StorageId", profile.ExpectedStorageId,
                inspection.AppMetadataStorageId);
            return issues.Count == 0
                ? new DatabaseProviderTestResult(true, "数据库版本、结构及身份检查通过。", inspection.UserVersion,
                    inspection.AppMetadataDatabaseInstanceId, inspection.AppMetadataStorageId)
                : new DatabaseProviderTestResult(false, string.Join("；", issues), inspection.UserVersion,
                    inspection.AppMetadataDatabaseInstanceId, inspection.AppMetadataStorageId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                                   or System.Data.SQLite.SQLiteException or InvalidOperationException)
        {
            return Failure("数据库只读检查失败：" + ex.Message, null);
        }
    }

    /// <summary>
    /// 重新检查后以 FailIfMissing 打开 v9；目标库消失时不会由连接池补建空文件。
    /// </summary>
    public async Task<DatabaseServices> OpenExistingAsync(
        DatabaseProfile profile,
        CancellationToken cancellationToken = default)
    {
        var check = await TestAsync(profile, cancellationToken);
        if (!check.Succeeded)
            throw new InvalidOperationException(check.Message);

        var path = GetPath(profile);
        var factory = new SqliteConnectionFactory(path, createDirectoryIfMissing: false, failIfMissing: true);
        try
        {
            var database = new SqliteDatabase(factory, _passwordHasher);
            await database.ValidateExistingAsync(cancellationToken);
            var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(path, cancellationToken);
            VerifyExpectedIdentity(profile, inspection);
            return CreateServices(factory, database, inspection);
        }
        catch
        {
            factory.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 显式建立全新 SQLite 文件。已有文件一律拒绝接管或覆盖。
    /// </summary>
    public async Task<DatabaseServices> CreateNewAsync(
        DatabaseProfile profile,
        string storageId,
        string initialAdministratorPassword,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(profile);
        if (File.Exists(path))
            throw new IOException($"数据库文件“{path}”已存在；新库初始化不会覆盖或接管现有文件。");
        var factory = new SqliteConnectionFactory(path, createDirectoryIfMissing: true, failIfMissing: false);
        try
        {
            var database = new SqliteDatabase(factory, _passwordHasher);
            await database.CreateNewAsync(storageId, initialAdministratorPassword, cancellationToken);
            var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(path, cancellationToken);
            var issues = SqliteSchemaV9.Validate(inspection);
            if (issues.Count > 0)
                throw new InvalidDataException("新数据库结构校验失败：" + string.Join("；", issues));
            return CreateServices(factory, database, inspection);
        }
        catch
        {
            factory.Dispose();
            throw;
        }
    }

    private string GetPath(DatabaseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(profile.ProviderKey, Key, StringComparison.Ordinal))
            throw new NotSupportedException($"未安装数据库 Provider：{profile.ProviderKey}");
        if (string.IsNullOrWhiteSpace(profile.SqlitePath) || !Path.IsPathFullyQualified(profile.SqlitePath))
            throw new InvalidDataException("SQLite 配置必须包含绝对数据库路径。");
        if (profile.ConnectionTimeoutSeconds is <= 0 or > 300)
            throw new InvalidDataException("SQLite 连接超时必须在 1 到 300 秒之间。");
        return Path.GetFullPath(profile.SqlitePath);
    }

    private static void AddIdentityIssue(
        ICollection<string> issues,
        string name,
        string? expected,
        string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
            issues.Add($"配置缺少 {name} 绑定");
        else if (!string.Equals(expected, actual, StringComparison.Ordinal))
            issues.Add($"{name} 不匹配");
    }

    private static void VerifyExpectedIdentity(DatabaseProfile profile, SqliteDatabaseInspection inspection)
    {
        var issues = new List<string>();
        AddIdentityIssue(issues, "DatabaseInstanceId", profile.ExpectedDatabaseInstanceId,
            inspection.AppMetadataDatabaseInstanceId);
        AddIdentityIssue(issues, "StorageId", profile.ExpectedStorageId, inspection.AppMetadataStorageId);
        if (issues.Count > 0) throw new InvalidDataException(string.Join("；", issues));
    }

    private static DatabaseProviderTestResult Failure(string message, SqliteDatabaseInspection? inspection)
        => new(false, message, inspection?.UserVersion, inspection?.AppMetadataDatabaseInstanceId,
            inspection?.AppMetadataStorageId);

    private static DatabaseServices CreateServices(
        ISqliteConnectionFactory factory,
        SqliteDatabase database,
        SqliteDatabaseInspection inspection)
    {
        var databaseInstanceId = inspection.AppMetadataDatabaseInstanceId
            ?? throw new InvalidDataException("数据库缺少 DatabaseInstanceId。");
        var storageId = inspection.AppMetadataStorageId
            ?? throw new InvalidDataException("数据库缺少 StorageId。");
        return new DatabaseServices(
            factory,
            database,
            databaseInstanceId,
            storageId,
            new UserRepository(factory),
            new ProductRepository(factory),
            new RecordRepository(factory),
            new TestPointRepository(factory),
            new ModelPointConfigRepository(factory),
            new TestParameterRepository(factory),
            new ReportRepository(factory),
            new BenchControlStateRepository(factory),
            new SqliteAuditLog(factory),
            new SqliteUnitOfWorkFactory(factory));
    }
}
