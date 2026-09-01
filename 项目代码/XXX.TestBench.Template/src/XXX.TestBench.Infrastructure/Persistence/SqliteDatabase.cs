using Microsoft.Data.Sqlite;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>SQLite 数据库初始化：完整性检查、正式业务 schema、种子数据。不迁移旧 management_records JSON 表。</summary>
public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = 1;

    private readonly ISqliteConnectionFactory _factory;
    private readonly IPasswordHasher _hasher;

    public SqliteDatabase(ISqliteConnectionFactory factory, IPasswordHasher hasher)
    {
        _factory = factory;
        _hasher = hasher;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA integrity_check";
            var result = (string)(await cmd.ExecuteScalarAsync(ct))!;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SQLite 完整性检查失败：{result}");
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='app_metadata'";
            var hasMetadata = (long)(await cmd.ExecuteScalarAsync(ct))! > 0;
            if (hasMetadata)
            {
                cmd.CommandText = "SELECT value FROM app_metadata WHERE key='schema_version'";
                var current = await cmd.ExecuteScalarAsync(ct);
                if (current is not null && Convert.ToInt32(current) >= CurrentSchemaVersion) return;
            }
        }

        await using var migration = conn.BeginTransaction();

        foreach (var statement in SchemaStatements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = migration;
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await migration.CommitAsync(ct);

        await SeedAsync(ct);
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        await using var conn = _factory.Open();
        await using var txn = conn.BeginTransaction();
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = txn;
                cmd.CommandText = "SELECT COUNT(1) FROM roles";
                if ((long)(await cmd.ExecuteScalarAsync(ct))! > 0) { await txn.CommitAsync(ct); return; }
            }

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

            // 初始管理员：admin / admin123，首次登录强制改密
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = txn;
                cmd.CommandText = "SELECT id FROM roles WHERE name='Administrator'";
                var roleId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
                cmd.CommandText = """
                    INSERT INTO users (login_name, display_name, password_hash, must_change_password, is_enabled, failed_login_count, locked_until_utc, role_id, created_at_utc)
                    VALUES ('admin', '管理员', $hash, 1, 1, 0, NULL, $role, $now)
                    """;
                cmd.Parameters.AddWithValue("$hash", _hasher.Hash("admin123"));
                cmd.Parameters.AddWithValue("$role", roleId);
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task InsertRoleAsync(SqliteConnection conn, SqliteTransaction txn, string name, IEnumerable<PermissionCode> permissions, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = "INSERT INTO roles (name) VALUES ($name)";
        cmd.Parameters.AddWithValue("$name", name);
        await cmd.ExecuteNonQueryAsync(ct);
        var roleId = (int)(long)(await LastIdAsync(conn, txn, ct));
        foreach (var p in permissions)
        {
            await using var pcmd = conn.CreateCommand();
            pcmd.Transaction = txn;
            pcmd.CommandText = "INSERT INTO role_permissions (role_id, permission_code) VALUES ($role, $perm)";
            pcmd.Parameters.AddWithValue("$role", roleId);
            pcmd.Parameters.AddWithValue("$perm", (int)p);
            await pcmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<long> LastIdAsync(SqliteConnection conn, SqliteTransaction txn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

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
}
