using System.Data.Common;
using FreeSql;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 负责创建数据库结构、执行版本迁移并写入初始角色。
/// </summary>
public sealed class SqliteDatabase
{
    /// <summary>
    /// 当前数据库结构版本号。
    /// </summary>
    public const int CurrentSchemaVersion = 5;

    /// <summary>
    /// 数据库连接工厂。
    /// </summary>
    private readonly ISqliteConnectionFactory _factory;
    /// <summary>
    /// 密码散列器，用于写入初始账号。
    /// </summary>
    private readonly IPasswordHasher _hasher;

    /// <summary>
    /// 创建数据库初始化器。
    /// </summary>
    public SqliteDatabase(ISqliteConnectionFactory factory, IPasswordHasher hasher)
    {
        _factory = factory;
        _hasher = hasher;
    }

    /// <summary>
    /// 初始化数据库：执行全部未完成的迁移并写入初始数据。
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        var conn = lease.Connection;
        var db = _factory.Db;

        var integrity = Convert.ToString(
            await db.Ado.ExecuteScalarAsync(conn, null, "PRAGMA integrity_check", new { }, ct));
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite 完整性检查失败：{integrity}");

        var currentVersion = Convert.ToInt32(
            await db.Ado.ExecuteScalarAsync(conn, null, "PRAGMA user_version", new { }, ct));
        foreach (var (version, statements) in Migrations.Where(m => m.Version > currentVersion).OrderBy(m => m.Version))
        {
            await using var txn = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var statement in statements)
                    await db.Ado.ExecuteNonQueryAsync(conn, txn, statement, new { }, ct);

                await db.Ado.ExecuteNonQueryAsync(conn, txn, $"PRAGMA user_version = {version}", new { }, ct);
                await txn.CommitAsync(ct);
            }
            catch
            {
                await txn.RollbackAsync(ct);
                throw;
            }
        }

        await SeedAsync(ct);
    }

    /// <summary>
    /// 写入初始角色与权限。
    /// </summary>
    private async Task SeedAsync(CancellationToken ct)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        var conn = lease.Connection;
        var db = _factory.Db;
        await using var txn = await conn.BeginTransactionAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var roleCount = db.Select<SqliteRole>()
                .WithConnection(conn)
                .WithTransaction(txn)
                .Count();
            if (roleCount == 0)
            {
                var all = Enum.GetValues<PermissionCode>();
                InsertRole(db, conn, txn, "Administrator", "Administrator", all, ct);
                InsertRole(db, conn, txn, "Operator", "Operator", new[]
                {
                    PermissionCode.ViewOverview, PermissionCode.ExecuteTests,
                    PermissionCode.ViewRecords, PermissionCode.ViewLogs
                }, ct);
                InsertRole(db, conn, txn, "Maintenance", "Maintenance", new[]
                {
                    PermissionCode.ViewOverview, PermissionCode.ManualControl, PermissionCode.ManageDevices,
                    PermissionCode.CalibrateDevices, PermissionCode.ViewRecords, PermissionCode.ViewLogs
                }, ct);

                var role = db.Select<SqliteRole>()
                    .WithConnection(conn)
                    .WithTransaction(txn)
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
                }).WithConnection(conn).WithTransaction(txn).ExecuteAffrows();
            }

            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// <summary>
    /// 插入一个角色及其权限。
    /// </summary>
    private static void InsertRole(
        IFreeSql db,
        DbConnection conn,
        DbTransaction txn,
        string name,
        string? systemKey,
        IEnumerable<PermissionCode> permissions,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var roleId = db.Insert(new SqliteRole
        {
            Name = name,
            SystemKey = systemKey
        }).WithConnection(conn).WithTransaction(txn).ExecuteIdentity();

        foreach (var permission in permissions)
        {
            ct.ThrowIfCancellationRequested();
            db.Insert(new SqliteRolePermission
            {
                RoleId = checked((int)roleId),
                PermissionCode = (int)permission
            }).WithConnection(conn).WithTransaction(txn).ExecuteAffrows();
        }
    }

    private static (int Version, string[] Statements)[] Migrations => new[] { (1, SchemaStatements), (2, Version2Statements), (3, Version3Statements), (4, Version4Statements), (5, Version5Statements) };

    private static readonly string[] SchemaStatements =
    {
        """
        CREATE TABLE IF NOT EXISTS app_metadata (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        )
        """,
        "INSERT OR REPLACE INTO app_metadata (key, value) VALUES ('schema_version', '1')",
        """
        CREATE TABLE IF NOT EXISTS roles (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS role_permissions (
            role_id INTEGER NOT NULL,
            permission_code INTEGER NOT NULL,
            PRIMARY KEY (role_id, permission_code),
            FOREIGN KEY (role_id) REFERENCES roles(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            login_name TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            password_hash TEXT NOT NULL,
            must_change_password INTEGER NOT NULL DEFAULT 1,
            is_enabled INTEGER NOT NULL DEFAULT 1,
            failed_login_count INTEGER NOT NULL DEFAULT 0,
            locked_until_utc TEXT NULL,
            role_id INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (role_id) REFERENCES roles(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS product_types (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            -- 保留旧版列，业务层使用 id；后续迁移可删除
            code TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            is_enabled INTEGER NOT NULL DEFAULT 1,
            created_at_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS product_models (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_type_id INTEGER NOT NULL,
            -- 保留旧版列，业务层使用 id；后续迁移可删除
            code TEXT NOT NULL,
            name TEXT NOT NULL,
            is_enabled INTEGER NOT NULL DEFAULT 1,
            created_at_utc TEXT NOT NULL,
            UNIQUE (product_type_id, code),
            FOREIGN KEY (product_type_id) REFERENCES product_types(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS test_item_definitions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            code TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            executor_code TEXT NOT NULL,
            result_kind TEXT NOT NULL,
            is_enabled INTEGER NOT NULL DEFAULT 1,
            sort_order INTEGER NOT NULL DEFAULT 0,
            created_at_utc TEXT NOT NULL
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS test_tasks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            task_number TEXT NOT NULL UNIQUE,
            product_model_id INTEGER NOT NULL,
            product_number TEXT NULL,
            batch_number TEXT NULL,
            station_number TEXT NULL,
            remark TEXT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            created_by_user_id INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            started_at_utc TEXT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (product_model_id) REFERENCES product_models(id),
            FOREIGN KEY (created_by_user_id) REFERENCES users(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS test_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            task_id INTEGER NOT NULL,
            parameter_snapshot TEXT NULL,
            device_mode INTEGER NOT NULL,
            operator_user_id INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            conclusion TEXT NULL,
            started_at_utc TEXT NOT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (task_id) REFERENCES test_tasks(id),
            FOREIGN KEY (operator_user_id) REFERENCES users(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS test_item_results (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            record_id INTEGER NOT NULL,
            recipe_item_id INTEGER NULL,
            test_item_definition_id INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            summary_value TEXT NULL,
            result_text TEXT NULL,
            started_at_utc TEXT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (record_id) REFERENCES test_records(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS report_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            test_record_id INTEGER NOT NULL,
            template_path TEXT NOT NULL,
            output_path TEXT NULL,
            status INTEGER NOT NULL DEFAULT 0,
            error TEXT NULL,
            created_by_user_id INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL,
            FOREIGN KEY (test_record_id) REFERENCES test_records(id),
            FOREIGN KEY (created_by_user_id) REFERENCES users(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS audit_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            actor TEXT NOT NULL,
            action TEXT NOT NULL,
            target TEXT NULL,
            detail TEXT NULL,
            created_at_utc TEXT NOT NULL
        )
        """
    };

    private static readonly string[] Version2Statements =
    {
        """
        CREATE TABLE IF NOT EXISTS project_test_parameters (
            scope_key TEXT PRIMARY KEY,
            test_time_seconds INTEGER NOT NULL,
            updated_by TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS product_type_test_parameters (
            product_type_id INTEGER PRIMARY KEY,
            test_voltage_v REAL NOT NULL,
            updated_by TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            FOREIGN KEY (product_type_id) REFERENCES product_types(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS product_model_test_parameters (
            product_model_id INTEGER PRIMARY KEY,
            protect_current_ma REAL NOT NULL,
            updated_by TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            FOREIGN KEY (product_model_id) REFERENCES product_models(id)
        )
        """,
        """
        CREATE TABLE test_tasks_v2 (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            task_number TEXT NOT NULL UNIQUE,
            product_model_id INTEGER NOT NULL,
            product_number TEXT NULL,
            batch_number TEXT NULL,
            station_number TEXT NULL,
            remark TEXT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            created_by_user_id INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            started_at_utc TEXT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (product_model_id) REFERENCES product_models(id),
            FOREIGN KEY (created_by_user_id) REFERENCES users(id)
        )
        """,
        """
        INSERT INTO test_tasks_v2 (id, task_number, product_model_id, product_number, batch_number, station_number, remark, state, created_by_user_id, created_at_utc, started_at_utc, finished_at_utc)
        SELECT id, task_number, product_model_id, product_number, batch_number, station_number, remark, state, created_by_user_id, created_at_utc, started_at_utc, finished_at_utc FROM test_tasks
        """,
        "DROP TABLE test_tasks",
        "ALTER TABLE test_tasks_v2 RENAME TO test_tasks",
        """
        CREATE TABLE test_records_v2 (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            task_id INTEGER NOT NULL,
            parameter_snapshot TEXT NULL,
            device_mode INTEGER NOT NULL,
            operator_user_id INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            conclusion TEXT NULL,
            started_at_utc TEXT NOT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (task_id) REFERENCES test_tasks(id),
            FOREIGN KEY (operator_user_id) REFERENCES users(id)
        )
        """,
        """
        INSERT INTO test_records_v2 (id, task_id, parameter_snapshot, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc)
        SELECT id, task_id, NULL, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc FROM test_records
        """,
        "DROP TABLE test_records",
        "ALTER TABLE test_records_v2 RENAME TO test_records",
        """
        CREATE TABLE test_item_results_v2 (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            record_id INTEGER NOT NULL,
            recipe_item_id INTEGER NULL,
            test_item_definition_id INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            summary_value TEXT NULL,
            result_text TEXT NULL,
            started_at_utc TEXT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (record_id) REFERENCES test_records(id)
        )
        """,
        """
        INSERT INTO test_item_results_v2 (id, record_id, recipe_item_id, test_item_definition_id, state, summary_value, result_text, started_at_utc, finished_at_utc)
        SELECT id, record_id, recipe_item_id, test_item_definition_id, state, summary_value, result_text, started_at_utc, finished_at_utc FROM test_item_results
        """,
        "DROP TABLE test_item_results",
        "ALTER TABLE test_item_results_v2 RENAME TO test_item_results"
    };

    private static readonly string[] Version3Statements =
    {
        "DROP TABLE IF EXISTS recipe_parameter_values",
        "DROP TABLE IF EXISTS recipe_items",
        "DROP TABLE IF EXISTS recipe_versions",
        "DROP TABLE IF EXISTS parameter_definitions",
        "ALTER TABLE test_item_results DROP COLUMN recipe_item_id"
    };

    private static readonly string[] Version4Statements =
    {
        """
        CREATE TABLE IF NOT EXISTS test_item_points (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_type_id INTEGER NOT NULL,
            -- 保留旧版列，业务层使用 id；后续迁移可删除
            code TEXT NOT NULL,
            name TEXT NOT NULL,
            executor_code TEXT NOT NULL,
            result_kind TEXT NOT NULL DEFAULT 'PassFail',
            is_enabled INTEGER NOT NULL DEFAULT 1,
            sort_order INTEGER NOT NULL DEFAULT 0,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            UNIQUE (product_type_id, code),
            FOREIGN KEY (product_type_id) REFERENCES product_types(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS model_point_configs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_model_id INTEGER NOT NULL,
            test_item_point_id INTEGER NOT NULL,
            sort_order INTEGER NOT NULL,
            configured_at_utc TEXT NOT NULL,
            UNIQUE (product_model_id, test_item_point_id),
            FOREIGN KEY (product_model_id) REFERENCES product_models(id),
            FOREIGN KEY (test_item_point_id) REFERENCES test_item_points(id)
        )
        """,
        """
        CREATE TABLE test_records_v4 (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            record_number TEXT NOT NULL UNIQUE,
            product_model_id INTEGER NOT NULL,
            product_number TEXT NULL,
            batch_number TEXT NULL,
            station_number TEXT NULL,
            remark TEXT NULL,
            parameter_snapshot TEXT NULL,
            sequence_snapshot TEXT NULL,
            device_mode INTEGER NOT NULL,
            operator_user_id INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            conclusion TEXT NULL,
            started_at_utc TEXT NOT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (product_model_id) REFERENCES product_models(id),
            FOREIGN KEY (operator_user_id) REFERENCES users(id)
        )
        """,
        """
        INSERT INTO test_records_v4 (id, record_number, product_model_id, product_number, batch_number, station_number, remark, parameter_snapshot, sequence_snapshot, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc)
        SELECT r.id, COALESCE(t.task_number, 'R-' || r.id), t.product_model_id, t.product_number, t.batch_number, t.station_number, t.remark, r.parameter_snapshot, NULL, r.device_mode, r.operator_user_id, r.state, r.conclusion, r.started_at_utc, r.finished_at_utc
        FROM test_records r LEFT JOIN test_tasks t ON t.id = r.task_id
        """,
        "DROP TABLE test_records",
        "ALTER TABLE test_records_v4 RENAME TO test_records",
        "DROP TABLE IF EXISTS test_item_results",
        """
        CREATE TABLE test_item_results (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            record_id INTEGER NOT NULL,
            test_item_point_id INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            summary_value TEXT NULL,
            result_text TEXT NULL,
            started_at_utc TEXT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (record_id) REFERENCES test_records(id)
        )
        """,
        "DROP TABLE IF EXISTS test_tasks",
        "DROP TABLE IF EXISTS test_item_definitions",
        "DELETE FROM role_permissions WHERE permission_code=2 AND role_id IN (SELECT id FROM roles WHERE name <> 'Administrator')"
    };

    private static readonly string[] Version5Statements =
    {
        "ALTER TABLE roles ADD COLUMN system_key TEXT NULL",
        "UPDATE roles SET system_key='Administrator' WHERE name='Administrator' AND system_key IS NULL",
        "UPDATE roles SET system_key='Operator' WHERE name='Operator' AND system_key IS NULL",
        "UPDATE roles SET system_key='Maintenance' WHERE name='Maintenance' AND system_key IS NULL",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_system_key ON roles(system_key) WHERE system_key IS NOT NULL",
        "INSERT OR IGNORE INTO role_permissions (role_id, permission_code) SELECT id, 14 FROM roles WHERE system_key='Administrator'"
    };

}
