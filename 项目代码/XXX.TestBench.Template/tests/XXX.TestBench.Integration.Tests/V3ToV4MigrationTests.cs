using System.Security.Cryptography;
using System.Data.SQLite;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

/// <summary>
/// 生产启动不执行历史 SQLite 迁移，只识别并拒绝旧版本。
/// </summary>
public sealed class UnsupportedSqliteSchemaTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    public async Task Initialize_RejectsUnsupportedVersion_WithoutChangingSource(int version)
    {
        using var env = TestEnv.Create();
        var connectionString = new SQLiteConnectionStringBuilder
        {
            DataSource = env.DbPath,
            Pooling = false,
            ForeignKeys = false
        }.ToString();
        using (var connection = new SQLiteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE preserved_data (id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT INTO preserved_data (id, value) VALUES (1, 'keep'); PRAGMA user_version = {version};";
            command.ExecuteNonQuery();
        }
        var before = SHA256.HashData(await File.ReadAllBytesAsync(env.DbPath));
        InvalidOperationException error;
        using (var factory = new SqliteConnectionFactory(env.DbPath))
        {
            error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new SqliteDatabase(factory, new Pbkdf2PasswordHasher()).ValidateExistingAsync());
        }
        var after = SHA256.HashData(await File.ReadAllBytesAsync(env.DbPath));

        Assert.NotEmpty(error.Message);
        Assert.Equal(before, after);
        var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(env.DbPath);
        Assert.Equal(version, inspection.UserVersion);
        Assert.Contains("preserved_data", inspection.Tables);
        Assert.DoesNotContain("users", inspection.Tables);
    }
}
