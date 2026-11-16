using System.Data.SQLite;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Infrastructure.Persistence;

namespace XXX.TestBench.App.Tests;

public sealed class ExistingDatabaseAdoptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingStorageIdentity_IsBackedUpAndRegistered(bool missingKey)
    {
        var root = Path.Combine(@"D:\Codex相关\首次启动身份修复测试", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "existing.db");
        var original = await new ApplicationBootstrapper(Path.Combine(root, "original"))
            .InitializeNewInstallationAsync("BENCH-01", path, Path.Combine(root, "storage"), "TestPassword123!");
        SQLiteConnection.ClearAllPools();
        Execute(path, missingKey ? "DELETE FROM app_metadata WHERE key='storage_id'"
            : "UPDATE app_metadata SET value='' WHERE key='storage_id'");
        var bootstrapper = new ApplicationBootstrapper(Path.Combine(root, "adopted"));
        var preview = await bootstrapper.ReadExistingDatabaseIdentityAsync(path);
        Assert.True(string.IsNullOrEmpty(preview.StorageId));
        var settings = await bootstrapper.AdoptExistingAsync("BENCH-01", path, Path.Combine(root, "storage"));
        Assert.Equal(original.ActiveProfile!.ExpectedDatabaseInstanceId, settings.ActiveProfile!.ExpectedDatabaseInstanceId);
        Assert.True(Guid.TryParse(settings.Storage!.StorageId, out _));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(bootstrapper.StateRoot, "database-backups"), "*.db"));
        Assert.True(string.IsNullOrEmpty((await SqliteDatabaseInspector.InspectReadOnlyAsync(backup)).AppMetadataStorageId));
        Assert.Empty(SqliteSchemaV9.Validate(await SqliteDatabaseInspector.InspectReadOnlyAsync(path)));
        using var running = await bootstrapper.BootstrapExistingAsync();
    }

    [Fact]
    public async Task ValidIdentity_IsPreserved_AndOtherDamageIsRejected()
    {
        var root = Path.Combine(@"D:\Codex相关\首次启动身份修复测试", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "existing.db");
        var original = await new ApplicationBootstrapper(Path.Combine(root, "original"))
            .InitializeNewInstallationAsync("BENCH-01", path, Path.Combine(root, "storage"), "TestPassword123!");
        SQLiteConnection.ClearAllPools();
        var backups = Path.Combine(root, "backups");
        await SqliteStorageIdentityInitializer.EnsureAsync(path, backups);
        Assert.False(Directory.Exists(backups));
        Assert.Equal(original.Storage!.StorageId, (await SqliteDatabaseInspector.InspectReadOnlyAsync(path)).AppMetadataStorageId);
        Execute(path, "UPDATE app_metadata SET value='' WHERE key='storage_id'; DROP TABLE alarm_events;");
        await Assert.ThrowsAsync<InvalidDataException>(() => SqliteStorageIdentityInitializer.EnsureAsync(path, backups));
        Assert.False(Directory.Exists(backups));
        Assert.Equal("", (await SqliteDatabaseInspector.InspectReadOnlyAsync(path)).AppMetadataStorageId);
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
            { DataSource = path, Pooling = false, FailIfMissing = true }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
