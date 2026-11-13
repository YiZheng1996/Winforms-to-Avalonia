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
    /// 只读预检现有文件；仅新路径或真正的空数据库可以直接创建。
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(_factory.DatabasePath, ct);
        var isNewDatabase = !inspection.Exists;
        if (inspection.Exists)
        {
            if (!string.Equals(inspection.IntegrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SQLite 完整性检查失败：{inspection.IntegrityCheck}；原文件未修改。");
            if (inspection.ForeignKeyErrors.Count > 0)
                throw new InvalidOperationException("SQLite 外键检查失败，原文件未修改：" + string.Join("；", inspection.ForeignKeyErrors));
            if (inspection.UserVersion == 0 && inspection.Objects.Count == 0)
                isNewDatabase = true;
            else if (inspection.UserVersion is 7 or 8)
                throw new InvalidOperationException($"数据库文件“{_factory.DatabasePath}”为 v{inspection.UserVersion}，请先使用离线升级工具生成 v9 副本；原文件未修改。");
            else if (inspection.UserVersion != CurrentSchemaVersion)
                throw new InvalidOperationException($"不支持的数据库版本或结构（user_version={inspection.UserVersion}）；原文件未修改。");
            else
            {
                var issues = SqliteSchemaV9.Validate(inspection);
                if (issues.Count > 0)
                    throw new InvalidOperationException("数据库 v9 结构校验失败，原文件未修改：" + string.Join("；", issues));
            }
        }

        if (!isNewDatabase)
            return;

        await CreateSchemaAndSeedAsync(ct);
    }

    private async Task CreateSchemaAndSeedAsync(CancellationToken ct)
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
                connection, transaction, "PRAGMA user_version = 9", new { }, ct);
            await SeedAsync(connection, transaction, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task SeedAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct)
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
            PasswordHash = _hasher.Hash("admin123"),
            MustChangePassword = 1,
            IsEnabled = 1,
            FailedLoginCount = 0,
            LockedUntilUtc = null,
            RoleId = role.Id,
            CreatedAtUtc = DateTime.UtcNow.ToString("O")
        }).WithConnection(connection).WithTransaction(transaction).ExecuteAffrows();
        await Task.CompletedTask;
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
