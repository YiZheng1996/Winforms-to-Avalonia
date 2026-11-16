using System.Data.Common;
using FreeSql;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 创建当前数据库 v9；旧数据库必须先经独立离线升级工具处理。
/// </summary>
public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = SqliteSchemaV9.Version;

    private readonly ISqliteConnectionFactory _factory;
    private readonly IPasswordHasher _hasher;

    public SqliteDatabase(ISqliteConnectionFactory factory, IPasswordHasher hasher)
    {
        _factory = factory;
        _hasher = hasher;
    }

    /// <summary>
    /// 兼容旧调用名，但现有库初始化只执行只读校验，不再创建缺失或空库。
    /// </summary>
    public Task InitializeAsync(CancellationToken ct = default) => ValidateExistingAsync(ct);

    /// <summary>
    /// 只读检查已存在的 v9 数据库；此方法不会创建目录、文件、表或元数据。
    /// </summary>
    public async Task ValidateExistingAsync(CancellationToken ct = default)
    {
        var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(_factory.DatabasePath, ct);
        ValidateExistingInspection(inspection, _factory.DatabasePath);
    }

    /// <summary>
    /// 仅供受控首次初始化使用；目标路径必须不存在，且初始管理员凭据与存储身份由调用方明确提供。
    /// </summary>
    public async Task CreateNewAsync(string storageId, string initialAdministratorPassword, CancellationToken ct = default)
    {
        if (!Guid.TryParseExact(storageId, "D", out var parsedStorageId)
            || !string.Equals(storageId, parsedStorageId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("StorageId 必须为小写 GUID D 格式", nameof(storageId));
        if (string.IsNullOrWhiteSpace(initialAdministratorPassword) || initialAdministratorPassword.Length < 8)
            throw new ArgumentException("初始管理员密码至少需要 8 个字符", nameof(initialAdministratorPassword));
        if (File.Exists(_factory.DatabasePath))
            throw new IOException($"数据库文件“{_factory.DatabasePath}”已存在；新库初始化不会覆盖或接管现有文件。");

        await CreateSchemaAndSeedAsync(storageId, initialAdministratorPassword, ct);
    }

    private async Task CreateSchemaAndSeedAsync(
        string storageId,
        string initialAdministratorPassword,
        CancellationToken ct)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        var connection = lease.Connection;
        var db = _factory.Db;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            foreach (var statement in SqliteSchemaV9.CreateStatements)
            {
                ct.ThrowIfCancellationRequested();
                await db.Ado.ExecuteNonQueryAsync(connection, transaction, statement, new { }, ct);
            }
            await db.Ado.ExecuteNonQueryAsync(
                connection, transaction,
                "INSERT INTO app_metadata (key, value) VALUES ('schema_version', '9')", new { }, ct);
            await db.Ado.ExecuteNonQueryAsync(
                connection, transaction,
                "INSERT INTO app_metadata (key, value) VALUES ('database_instance_id', @value)",
                new { value = Guid.NewGuid().ToString("D").ToLowerInvariant() }, ct);
            await db.Ado.ExecuteNonQueryAsync(
                connection, transaction,
                "INSERT INTO app_metadata (key, value) VALUES ('storage_id', @value)",
                new { value = storageId }, ct);
            await db.Ado.ExecuteNonQueryAsync(
                connection, transaction, "PRAGMA user_version = 9", new { }, ct);
            await SeedAsync(connection, transaction, initialAdministratorPassword, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task SeedAsync(
        DbConnection connection,
        DbTransaction transaction,
        string initialAdministratorPassword,
        CancellationToken ct)
    {
        var db = _factory.Db;
        ct.ThrowIfCancellationRequested();
        var roleCount = db.Select<SqliteRole>()
            .WithConnection(connection)
            .WithTransaction(transaction)
            .Count();
        if (roleCount != 0)
            return;

        var all = PermissionCatalog.ActiveCodes;
        InsertRole(db, connection, transaction, "Administrator", "Administrator", all, ct);
        InsertRole(db, connection, transaction, "Operator", "Operator", new[]
        {
            PermissionCode.ViewOverview, PermissionCode.ExecuteTests,
            PermissionCode.ViewRecords, PermissionCode.ViewLogs
        }, ct);
        InsertRole(db, connection, transaction, "Maintenance", "Maintenance", new[]
        {
            PermissionCode.ViewOverview, PermissionCode.ManualControl, PermissionCode.ManageDevices,
            PermissionCode.CalibrateDevices, PermissionCode.ViewRecords, PermissionCode.ViewLogs
        }, ct);

        var role = db.Select<SqliteRole>()
            .WithConnection(connection)
            .WithTransaction(transaction)
            .Where(x => x.Name == "Administrator")
            .ToOne() ?? throw new InvalidOperationException("初始化 Administrator 角色失败。");
        db.Insert(new SqliteUser
        {
            LoginName = "admin",
            DisplayName = "管理员",
            PasswordHash = _hasher.Hash(initialAdministratorPassword),
            MustChangePassword = 1,
            IsEnabled = 1,
            FailedLoginCount = 0,
            LockedUntilUtc = null,
            RoleId = role.Id,
            CreatedAtUtc = DateTime.UtcNow.ToString("O")
        }).WithConnection(connection).WithTransaction(transaction).ExecuteAffrows();
        await Task.CompletedTask;
    }

    private static void ValidateExistingInspection(SqliteDatabaseInspection inspection, string databasePath)
    {
        if (!inspection.Exists)
            throw new FileNotFoundException($"数据库文件“{databasePath}”不存在；启动检查不会创建新库。", databasePath);
        if (!string.Equals(inspection.IntegrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite 完整性检查失败：{inspection.IntegrityCheck}；原文件未修改。");
        if (inspection.ForeignKeyErrors.Count > 0)
            throw new InvalidOperationException("SQLite 外键检查失败，原文件未修改：" + string.Join("；", inspection.ForeignKeyErrors));
        if (inspection.UserVersion is 7 or 8)
            throw new InvalidOperationException($"数据库文件“{databasePath}”为 v{inspection.UserVersion}，请先使用独立离线升级工具生成 v9 副本；原文件未修改。");
        if (inspection.UserVersion != CurrentSchemaVersion)
            throw new InvalidOperationException($"不支持的数据库版本或结构（user_version={inspection.UserVersion}）；原文件未修改。");
        var issues = SqliteSchemaV9.Validate(inspection);
        if (issues.Count > 0)
            throw new InvalidOperationException("数据库 v9 结构校验失败，原文件未修改：" + string.Join("；", issues));
    }

    private static void InsertRole(
        IFreeSql db,
        DbConnection connection,
        DbTransaction transaction,
        string name,
        string systemKey,
        IEnumerable<PermissionCode> permissions,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var roleId = db.Insert(new SqliteRole
        {
            Name = name,
            SystemKey = systemKey
        }).WithConnection(connection).WithTransaction(transaction).ExecuteIdentity();

        foreach (var permission in permissions)
        {
            ct.ThrowIfCancellationRequested();
            db.Insert(new SqliteRolePermission
            {
                RoleId = checked((int)roleId),
                PermissionCode = (int)permission
            }).WithConnection(connection).WithTransaction(transaction).ExecuteAffrows();
        }
    }
}
