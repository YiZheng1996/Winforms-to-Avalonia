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

        Assert.Equal(1L, Convert.ToInt64(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM users WHERE login_name='admin'", new { }, default)));
        Assert.Equal(3L, Convert.ToInt64(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM roles", new { }, default)));
        Assert.Equal("Administrator", Convert.ToString(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT system_key FROM roles WHERE name='Administrator'", new { }, default)));
        Assert.Equal(1L, Convert.ToInt64(await factory.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM role_permissions rp JOIN roles r ON r.id=rp.role_id WHERE r.name='Administrator' AND rp.permission_code=14",
            new { }, default)));
        Assert.Equal("ok", Convert.ToString(await factory.Db.Ado.ExecuteScalarAsync(
            "PRAGMA integrity_check", new { }, default)));

        // 第二次初始化（重启回读）：schema 已存在，种子不重复
        var factory2 = new SqliteConnectionFactory(env.DbPath);
        var db2 = new SqliteDatabase(factory2, hasher);
        await db2.InitializeAsync();

        Assert.Equal(1L, Convert.ToInt64(await factory2.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM users", new { }, default)));
    }

    [Fact]
    public async Task AdminSeedPassword_VerifiesWithHasher()
    {
        using var env = TestEnv.Create();
        var hasher = new Pbkdf2PasswordHasher();
        var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, hasher).InitializeAsync();

        var row = (await factory.Db.Ado.QueryAsync<AdminSeedRow>(
            "SELECT password_hash AS PasswordHash, must_change_password AS MustChangePassword FROM users WHERE login_name='admin'",
            new { }, default)).Single();
        Assert.True(hasher.Verify("admin123", row.PasswordHash));
        Assert.Equal(1, row.MustChangePassword); // 首次登录强制改密
    }

    private sealed class AdminSeedRow
    {
        public string PasswordHash { get; set; } = string.Empty;
        public int MustChangePassword { get; set; }
    }
}
