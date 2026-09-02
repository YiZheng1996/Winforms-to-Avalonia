using System.Data.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// FreeSql SQLite database initializer: integrity check, versioned schema and seed data.
/// </summary>
public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = 2;

    private readonly ISqliteConnectionFactory _factory;
    private readonly IPasswordHasher _hasher;

    public SqliteDatabase(ISqliteConnectionFactory factory, IPasswordHasher hasher)
    {
        _factory = factory;
        _hasher = hasher;
    }

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

    private async Task SeedAsync(CancellationToken ct)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        var conn = lease.Connection;
        var db = _factory.Db;
        await using var txn = await conn.BeginTransactionAsync(ct);
        try
        {
            var roleCount = Convert.ToInt64(
                await db.Ado.ExecuteScalarAsync(conn, txn, "SELECT COUNT(1) FROM roles", new { }, ct));
            if (roleCount == 0)
            {
                var all = Enum.GetValues<PermissionCode>();
                await InsertRoleAsync(conn, txn, "Administrator", all, ct);
                await InsertRoleAsync(conn, txn, "Operator", new[]
                {
                    PermissionCode.ViewOverview, PermissionCode.ManageTasks, PermissionCode.ExecuteTests,
                    PermissionCode.ViewRecords, PermissionCode.ViewLogs
                }, ct);
                await InsertRoleAsync(conn, txn, "Maintenance", new[]
                {
                    PermissionCode.ViewOverview, PermissionCode.ManualControl, PermissionCode.ManageDevices,
                    PermissionCode.CalibrateDevices, PermissionCode.ViewRecords, PermissionCode.ViewLogs
                }, ct);

                var roleId = Convert.ToInt32(await db.Ado.ExecuteScalarAsync(
                    conn, txn, "SELECT id FROM roles WHERE name='Administrator'", new { }, ct));
                await db.Ado.ExecuteNonQueryAsync(conn, txn, """
                    INSERT INTO users (login_name, display_name, password_hash, must_change_password, is_enabled, failed_login_count, locked_until_utc, role_id, created_at_utc)
                    VALUES ('admin', '管理员', @hash, 1, 1, 0, NULL, @role, @now)
                    """, new
                {
                    hash = _hasher.Hash("admin123"),
                    role = roleId,
                    now = DateTime.UtcNow.ToString("O")
                }, ct);
            }

            await EnsureBuiltInDefinitionsAsync(conn, txn, ct);
            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// 按代码把固定试验项序列种子化到试验项定义表（幂等）；模板执行流程由代码固定。
    /// </summary>
    private async Task EnsureBuiltInDefinitionsAsync(
        DbConnection conn,
        DbTransaction txn,
        CancellationToken ct)
    {
        var db = _factory.Db;
        foreach (var item in XXX.TestBench.Core.Domain.TestDefinitions.BuiltInTestSequence.Items)
        {
            await db.Ado.ExecuteNonQueryAsync(conn, txn, """
                INSERT INTO test_item_definitions (code, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc)
                SELECT @code, @name, @executor, @resultKind, 1, @sortOrder, @now
                WHERE NOT EXISTS (SELECT 1 FROM test_item_definitions WHERE code = @code)
                """, new
            {
                code = item.Code,
                name = item.Name,
                executor = item.ExecutorCode,
                resultKind = item.ResultKind,
                sortOrder = item.SortOrder,
                now = DateTime.UtcNow.ToString("O")
            }, ct);
        }
    }
    private async Task InsertRoleAsync(
        DbConnection conn,
        DbTransaction txn,
        string name,
        IEnumerable<PermissionCode> permissions,
        CancellationToken ct)
    {
        var db = _factory.Db;
        await db.Ado.ExecuteNonQueryAsync(
            conn, txn, "INSERT INTO roles (name) VALUES (@name)", new { name }, ct);
        var roleId = Convert.ToInt32(await db.Ado.ExecuteScalarAsync(
            conn, txn, "SELECT last_insert_rowid()", new { }, ct));

        foreach (var permission in permissions)
            await db.Ado.ExecuteNonQueryAsync(
                conn,
                txn,
                "INSERT INTO role_permissions (role_id, permission_code) VALUES (@role, @permission)",
                new { role = roleId, permission = (int)permission },
                ct);
    }

    private static (int Version, string[] Statements)[] Migrations => new[] { (1, SchemaStatements), (2, Version2Statements) };

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
        CREATE TABLE IF NOT EXISTS parameter_definitions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            test_item_definition_id INTEGER NOT NULL,
            code TEXT NOT NULL,
            name TEXT NOT NULL,
            data_type INTEGER NOT NULL,
            unit TEXT NULL,
            is_required INTEGER NOT NULL DEFAULT 0,
            min_value TEXT NULL,
            max_value TEXT NULL,
            precision INTEGER NULL,
            allowed_values TEXT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            UNIQUE (test_item_definition_id, code),
            FOREIGN KEY (test_item_definition_id) REFERENCES test_item_definitions(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS recipe_versions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_model_id INTEGER NOT NULL,
            version INTEGER NOT NULL,
            status INTEGER NOT NULL DEFAULT 0,
            name TEXT NOT NULL,
            report_template_path TEXT NULL,
            created_by_user_id INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            published_by_user_id INTEGER NULL,
            published_at_utc TEXT NULL,
            retired_by_user_id INTEGER NULL,
            retired_at_utc TEXT NULL,
            UNIQUE (product_model_id, version),
            FOREIGN KEY (product_model_id) REFERENCES product_models(id),
            FOREIGN KEY (created_by_user_id) REFERENCES users(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS recipe_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            recipe_version_id INTEGER NOT NULL,
            test_item_definition_id INTEGER NOT NULL,
            sort_order INTEGER NOT NULL,
            is_enabled INTEGER NOT NULL DEFAULT 1,
            UNIQUE (recipe_version_id, sort_order),
            FOREIGN KEY (recipe_version_id) REFERENCES recipe_versions(id),
            FOREIGN KEY (test_item_definition_id) REFERENCES test_item_definitions(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS recipe_parameter_values (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            recipe_item_id INTEGER NOT NULL,
            parameter_definition_id INTEGER NOT NULL,
            raw_value TEXT NOT NULL,
            UNIQUE (recipe_item_id, parameter_definition_id),
            FOREIGN KEY (recipe_item_id) REFERENCES recipe_items(id),
            FOREIGN KEY (parameter_definition_id) REFERENCES parameter_definitions(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS test_tasks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            task_number TEXT NOT NULL UNIQUE,
            product_model_id INTEGER NOT NULL,
            recipe_version_id INTEGER NOT NULL,
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
            FOREIGN KEY (recipe_version_id) REFERENCES recipe_versions(id),
            FOREIGN KEY (created_by_user_id) REFERENCES users(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS test_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            task_id INTEGER NOT NULL,
            recipe_version_id INTEGER NOT NULL,
            recipe_version_number INTEGER NOT NULL,
            device_mode INTEGER NOT NULL,
            operator_user_id INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            conclusion TEXT NULL,
            started_at_utc TEXT NOT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (task_id) REFERENCES test_tasks(id),
            FOREIGN KEY (recipe_version_id) REFERENCES recipe_versions(id),
            FOREIGN KEY (operator_user_id) REFERENCES users(id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS test_item_results (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            record_id INTEGER NOT NULL,
            recipe_item_id INTEGER NOT NULL,
            test_item_definition_id INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            summary_value TEXT NULL,
            result_text TEXT NULL,
            started_at_utc TEXT NULL,
            finished_at_utc TEXT NULL,
            FOREIGN KEY (record_id) REFERENCES test_records(id),
            FOREIGN KEY (recipe_item_id) REFERENCES recipe_items(id)
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
}