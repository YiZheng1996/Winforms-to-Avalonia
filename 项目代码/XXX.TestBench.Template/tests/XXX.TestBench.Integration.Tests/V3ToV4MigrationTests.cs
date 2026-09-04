using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

/// <summary>
/// schema v3 → v4 迁移验证：旧任务/记录数据按新语义迁移，任务表删除，权限回收，
/// 新项点表与配置表创建。这是“移除任务系统”数据兼容性的回归护栏。
/// </summary>
public class V3ToV4MigrationTests
{
    [Fact]
    public async Task V3Database_UpgradesToV4_PreservesRecords_AndDropsTasks()
    {
        using var env = TestEnv.Create();
        var hasher = new Pbkdf2PasswordHasher();
        var factory = new SqliteConnectionFactory(env.DbPath);
        await CreateV3DatabaseAsync(factory);

        await new SqliteDatabase(factory, hasher).InitializeAsync();

        // 版本号
        var version = Convert.ToInt64(await factory.Db.Ado.ExecuteScalarAsync("PRAGMA user_version", new { }, default));
        Assert.Equal(4L, version);

        // 记录迁移：task_number → record_number，产品标识保留
        var recordNumber = Convert.ToString(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT record_number FROM test_records WHERE id=1", new { }, default));
        Assert.Equal("T-LEGACY-0001", recordNumber);
        var productNumber = Convert.ToString(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT product_number FROM test_records WHERE id=1", new { }, default));
        Assert.Equal("SN001", productNumber);
        var modelId = Convert.ToInt64(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT product_model_id FROM test_records WHERE id=1", new { }, default));
        Assert.Equal(1L, modelId);

        // 任务表与旧全局项点定义表已删除；新表已创建
        Assert.Equal(0L, TableCountAsync(factory, "test_tasks"));
        Assert.Equal(0L, TableCountAsync(factory, "test_item_definitions"));
        Assert.Equal(1L, TableCountAsync(factory, "test_item_points"));
        Assert.Equal(1L, TableCountAsync(factory, "model_point_configs"));

        // 旧项点结果按新语义已重建为空表（旧定义无法映射）
        var resultCount = Convert.ToInt64(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM test_item_results", new { }, default));
        Assert.Equal(0L, resultCount);

        // Operator 角色不再持有原任务权限（数值 2）
        var operatorPermission2 = Convert.ToInt64(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM role_permissions rp JOIN roles r ON r.id=rp.role_id WHERE r.name='Operator' AND rp.permission_code=2",
            new { }, default));
        Assert.Equal(0L, operatorPermission2);
    }

    private static long TableCountAsync(SqliteConnectionFactory factory, string name)
    {
        var value = factory.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@name",
            new { name }, default).GetAwaiter().GetResult();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task CreateV3DatabaseAsync(SqliteConnectionFactory factory)
    {
        await using var lease = await factory.OpenLeaseAsync();
        var conn = lease.Connection;
        var db = factory.Db;
        foreach (var sql in V3Statements)
            await db.Ado.ExecuteNonQueryAsync(conn, null, sql, new { }, default);
    }

    private static readonly string[] V3Statements =
    {
        """
        CREATE TABLE roles (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE)
        """,
        """
        CREATE TABLE role_permissions (role_id INTEGER NOT NULL, permission_code INTEGER NOT NULL, PRIMARY KEY (role_id, permission_code))
        """,
        """
        CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, login_name TEXT NOT NULL UNIQUE, display_name TEXT NOT NULL, password_hash TEXT NOT NULL, must_change_password INTEGER NOT NULL DEFAULT 1, is_enabled INTEGER NOT NULL DEFAULT 1, failed_login_count INTEGER NOT NULL DEFAULT 0, locked_until_utc TEXT NULL, role_id INTEGER NOT NULL, created_at_utc TEXT NOT NULL)
        """,
        """
        CREATE TABLE product_types (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, is_enabled INTEGER NOT NULL DEFAULT 1, created_at_utc TEXT NOT NULL)
        """,
        """
        CREATE TABLE product_models (id INTEGER PRIMARY KEY AUTOINCREMENT, product_type_id INTEGER NOT NULL, code TEXT NOT NULL, name TEXT NOT NULL, is_enabled INTEGER NOT NULL DEFAULT 1, created_at_utc TEXT NOT NULL, UNIQUE (product_type_id, code))
        """,
        """
        CREATE TABLE test_item_definitions (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, executor_code TEXT NOT NULL, result_kind TEXT NOT NULL, is_enabled INTEGER NOT NULL DEFAULT 1, sort_order INTEGER NOT NULL DEFAULT 0, created_at_utc TEXT NOT NULL)
        """,
        """
        CREATE TABLE test_tasks (id INTEGER PRIMARY KEY AUTOINCREMENT, task_number TEXT NOT NULL UNIQUE, product_model_id INTEGER NOT NULL, product_number TEXT NULL, batch_number TEXT NULL, station_number TEXT NULL, remark TEXT NULL, state INTEGER NOT NULL DEFAULT 0, created_by_user_id INTEGER NOT NULL, created_at_utc TEXT NOT NULL, started_at_utc TEXT NULL, finished_at_utc TEXT NULL)
        """,
        """
        CREATE TABLE test_records (id INTEGER PRIMARY KEY AUTOINCREMENT, task_id INTEGER NOT NULL, parameter_snapshot TEXT NULL, device_mode INTEGER NOT NULL, operator_user_id INTEGER NOT NULL, state INTEGER NOT NULL DEFAULT 0, conclusion TEXT NULL, started_at_utc TEXT NOT NULL, finished_at_utc TEXT NULL)
        """,
        """
        CREATE TABLE test_item_results (id INTEGER PRIMARY KEY AUTOINCREMENT, record_id INTEGER NOT NULL, test_item_definition_id INTEGER NOT NULL, state INTEGER NOT NULL DEFAULT 0, summary_value TEXT NULL, result_text TEXT NULL, started_at_utc TEXT NULL, finished_at_utc TEXT NULL)
        """,
        "INSERT INTO roles (id, name) VALUES (1, 'Administrator')",
        "INSERT INTO roles (id, name) VALUES (2, 'Operator')",
        "INSERT INTO role_permissions (role_id, permission_code) VALUES (2, 2)",
        "INSERT INTO role_permissions (role_id, permission_code) VALUES (2, 3)",
        """
        INSERT INTO users (id, login_name, display_name, password_hash, must_change_password, is_enabled, role_id, created_at_utc)
        VALUES (1, 'admin', '管理员', 'x', 1, 1, 1, '2026-09-01T00:00:00.0000000Z')
        """,
        "INSERT INTO product_types (id, code, name, is_enabled, created_at_utc) VALUES (1, 'PT', '压力试验', 1, '2026-09-01T00:00:00.0000000Z')",
        "INSERT INTO product_models (id, product_type_id, code, name, is_enabled, created_at_utc) VALUES (1, 1, 'M1', '型号1', 1, '2026-09-01T00:00:00.0000000Z')",
        """
        INSERT INTO test_tasks (id, task_number, product_model_id, product_number, batch_number, station_number, remark, state, created_by_user_id, created_at_utc)
        VALUES (1, 'T-LEGACY-0001', 1, 'SN001', 'B001', 'S1', '旧记录', 3, 1, '2026-09-01T00:00:00.0000000Z')
        """,
        """
        INSERT INTO test_records (id, task_id, parameter_snapshot, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc)
        VALUES (1, 1, '{"snapshot":1}', 0, 1, 1, '通过', '2026-09-01T00:00:00.0000000Z', '2026-09-01T00:01:00.0000000Z')
        """,
        """
        INSERT INTO test_item_results (id, record_id, test_item_definition_id, state, summary_value, result_text, started_at_utc, finished_at_utc)
        VALUES (1, 1, 1, 2, '10', '通过', '2026-09-01T00:00:30.0000000Z', '2026-09-01T00:00:40.0000000Z')
        """,
        "PRAGMA user_version = 3"
    };
}
