using System.Data.SQLite;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 首次登记时为结构完整、仅缺少存储身份的数据库补齐身份；备份失败则不写入。
/// </summary>
public static class SqliteStorageIdentityInitializer
{
    public static IReadOnlyList<string> ValidateForAdoption(SqliteDatabaseInspection inspection)
        => SqliteSchemaV9.Validate(string.IsNullOrEmpty(inspection.AppMetadataStorageId)
            ? inspection with { AppMetadataStorageId = Guid.Empty.ToString("D") }
            : inspection);

    public static async Task EnsureAsync(string databasePath, string backupDirectory, CancellationToken ct = default)
    {
        var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(databasePath, ct);
        var issues = ValidateForAdoption(inspection);
        if (issues.Count > 0)
            throw new InvalidDataException("数据库检查未通过：" + string.Join("；", issues));
        if (!string.IsNullOrEmpty(inspection.AppMetadataStorageId))
            return;

        ct.ThrowIfCancellationRequested();
        using var source = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath), FailIfMissing = true, Pooling = false
        }.ToString());
        source.Open();
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory,
            $"before-storage-identity-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.db");
        // 使用 SQLite 快照备份，包含 WAL 中已提交的数据，不能直接复制正在使用的数据库文件。
        using (var backup = new SQLiteConnection(new SQLiteConnectionStringBuilder
               { DataSource = backupPath, Pooling = false }.ToString()))
        {
            backup.Open();
            source.BackupDatabase(backup, "main", "main", -1, null, 0);
        }
        var saved = await SqliteDatabaseInspector.InspectReadOnlyAsync(backupPath, ct);
        if (ValidateForAdoption(saved).Count > 0
            || saved.AppMetadataDatabaseInstanceId != inspection.AppMetadataDatabaseInstanceId)
            throw new InvalidDataException("数据库备份校验失败，未补齐存储身份。");

        ct.ThrowIfCancellationRequested();
        using var transaction = source.BeginTransaction();
        using var command = source.CreateCommand();
        command.Transaction = transaction;
        // 只补空值或缺失键，绝不覆盖其他登记流程已写入的身份。
        command.CommandText = "INSERT INTO app_metadata(key,value) VALUES('storage_id',@id) "
            + "ON CONFLICT(key) DO UPDATE SET value=excluded.value WHERE app_metadata.value=''";
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("D"));
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
