using Microsoft.Data.Sqlite;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public class SqliteDatabaseTests
{
    [Fact]
    public async Task Initialize_CreatesSchema_SeedsAdmin_AndSurvivesRestart()
    {
        using var env = TestEnv.Create();
        var hasher = new Pbkdf2PasswordHasher();

        // 第一次初始化
        var factory = new SqliteConnectionFactory(env.DbPath);
        var db = new SqliteDatabase(factory, hasher);
        await db.InitializeAsync();

        await using (var conn = factory.Open())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM users WHERE login_name='admin'";
            Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
            cmd.CommandText = "SELECT COUNT(1) FROM roles";
            Assert.Equal(3L, (long)(await cmd.ExecuteScalarAsync())!);
            cmd.CommandText = "PRAGMA integrity_check";
            Assert.Equal("ok", (string)(await cmd.ExecuteScalarAsync())!);
        }

        // 第二次初始化（重启回读）：schema 已存在，种子不重复
        var factory2 = new SqliteConnectionFactory(env.DbPath);
        var db2 = new SqliteDatabase(factory2, hasher);
        await db2.InitializeAsync();

        await using (var conn = factory2.Open())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM users";
            Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task AdminSeedPassword_VerifiesWithHasher()
    {
        using var env = TestEnv.Create();
        var hasher = new Pbkdf2PasswordHasher();
        var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, hasher).InitializeAsync();

        await using var conn = factory.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash, must_change_password FROM users WHERE login_name='admin'";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var hash = reader.GetString(0);
        Assert.True(hasher.Verify("admin123", hash));
        Assert.Equal(1, reader.GetInt32(1)); // 首次登录强制改密
    }
}
