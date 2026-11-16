using System.Data.Common;
using System.Data.SQLite;

namespace XXX.TestBench.Infrastructure.Persistence;

public sealed record SqliteColumnInfo(string Name, string Type, bool NotNull, bool IsPrimaryKey);
public sealed record SqliteForeignKeyInfo(string FromTable, string Table, string FromColumn, string ToColumn);
public sealed record SqliteIndexInfo(string Name, string Table, bool IsUnique, bool IsPartial, IReadOnlyList<string> Columns);
public sealed record SqliteSchemaObjectInfo(string Type, string Name, string Table, string? Sql);

/// <summary>
/// 不通过生产连接池执行只读数据库预检。
/// </summary>
public sealed record SqliteDatabaseInspection(
    bool Exists,
    int UserVersion,
    string IntegrityCheck,
    string? AppMetadataSchemaVersion,
    string? AppMetadataDatabaseInstanceId,
    string? AppMetadataStorageId,
    IReadOnlyList<SqliteSchemaObjectInfo> Objects,
    IReadOnlyList<string> Tables,
    IReadOnlyDictionary<string, IReadOnlyList<SqliteColumnInfo>> Columns,
    IReadOnlyList<SqliteForeignKeyInfo> ForeignKeys,
    IReadOnlyList<SqliteIndexInfo> Indexes,
    IReadOnlyList<string> ForeignKeyErrors);

public static class SqliteDatabaseInspector
{
    public static Task<SqliteDatabaseInspection> InspectReadOnlyAsync(
        string databasePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ct.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
            return Task.FromResult(new SqliteDatabaseInspection(false, 0, "不存在", null, null, null,
                Array.Empty<SqliteSchemaObjectInfo>(), Array.Empty<string>(),
                new Dictionary<string, IReadOnlyList<SqliteColumnInfo>>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<SqliteForeignKeyInfo>(), Array.Empty<SqliteIndexInfo>(), Array.Empty<string>()));

        var connectionString = new SQLiteConnectionStringBuilder
        {
            DataSource = fullPath,
            ReadOnly = true,
            Pooling = false,
            ForeignKeys = false
        }.ToString();
        using var connection = new SQLiteConnection(connectionString);
        connection.Open();

        var userVersion = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), System.Globalization.CultureInfo.InvariantCulture);
        var integrity = Convert.ToString(Scalar(connection, "PRAGMA integrity_check"), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var objects = ReadObjects(connection);
        var tables = objects
            .Where(item => item.Type.Equals("table", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columns = new Dictionary<string, IReadOnlyList<SqliteColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        var foreignKeys = new List<SqliteForeignKeyInfo>();
        var indexes = new List<SqliteIndexInfo>();
        foreach (var table in tables)
        {
            var safeTable = QuoteIdentifier(table);
            columns[table] = ReadColumns(connection, safeTable);
            foreignKeys.AddRange(ReadForeignKeys(connection, safeTable));
            indexes.AddRange(ReadIndexes(connection, table, safeTable));
        }

        string? metadataVersion = null;
        string? databaseInstanceId = null;
        string? storageId = null;
        if (tables.Contains("app_metadata", StringComparer.OrdinalIgnoreCase)
            && columns.TryGetValue("app_metadata", out var metadataColumns)
            && metadataColumns.Any(column => column.Name.Equals("key", StringComparison.OrdinalIgnoreCase))
            && metadataColumns.Any(column => column.Name.Equals("value", StringComparison.OrdinalIgnoreCase)))
            metadataVersion = Convert.ToString(Scalar(connection, "SELECT value FROM app_metadata WHERE key = 'schema_version' LIMIT 1"), System.Globalization.CultureInfo.InvariantCulture);
        if (tables.Contains("app_metadata", StringComparer.OrdinalIgnoreCase)
            && columns.TryGetValue("app_metadata", out metadataColumns)
            && metadataColumns.Any(column => column.Name.Equals("key", StringComparison.OrdinalIgnoreCase))
            && metadataColumns.Any(column => column.Name.Equals("value", StringComparison.OrdinalIgnoreCase)))
            databaseInstanceId = Convert.ToString(Scalar(connection, "SELECT value FROM app_metadata WHERE key = 'database_instance_id' LIMIT 1"), System.Globalization.CultureInfo.InvariantCulture);
        if (tables.Contains("app_metadata", StringComparer.OrdinalIgnoreCase)
            && columns.TryGetValue("app_metadata", out metadataColumns)
            && metadataColumns.Any(column => column.Name.Equals("key", StringComparison.OrdinalIgnoreCase))
            && metadataColumns.Any(column => column.Name.Equals("value", StringComparison.OrdinalIgnoreCase)))
            storageId = Convert.ToString(Scalar(connection, "SELECT value FROM app_metadata WHERE key = 'storage_id' LIMIT 1"), System.Globalization.CultureInfo.InvariantCulture);

        var foreignKeyErrors = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                foreignKeyErrors.Add($"table={reader.GetValue(0)}, rowid={reader.GetValue(1)}, parent={reader.GetValue(2)}, fkid={reader.GetValue(3)}");
        }

        return Task.FromResult(new SqliteDatabaseInspection(
            true, userVersion, integrity, metadataVersion, databaseInstanceId, storageId,
            objects, tables, columns, foreignKeys, indexes, foreignKeyErrors));
    }

    private static object? Scalar(SQLiteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static IReadOnlyList<SqliteSchemaObjectInfo> ReadObjects(SQLiteConnection connection)
    {
        var values = new List<SqliteSchemaObjectInfo>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type, name, tbl_name, sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return values;
    }

    private static IReadOnlyList<SqliteColumnInfo> ReadColumns(SQLiteConnection connection, string table)
    {
        var columns = new List<SqliteColumnInfo>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(new(reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0, reader.GetInt32(5) != 0));
        return columns;
    }

    private static IReadOnlyList<SqliteForeignKeyInfo> ReadForeignKeys(SQLiteConnection connection, string table)
    {
        var foreignKeys = new List<SqliteForeignKeyInfo>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            foreignKeys.Add(new(table.Trim('"'), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return foreignKeys;
    }

    private static IReadOnlyList<SqliteIndexInfo> ReadIndexes(SQLiteConnection connection, string tableName, string table)
    {
        var result = new List<SqliteIndexInfo>();
        var indexDefinitions = new List<(string Name, bool IsUnique, bool IsPartial)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({table})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                indexDefinitions.Add((reader.GetString(1), reader.GetInt32(2) != 0, reader.FieldCount > 4 && reader.GetInt32(4) != 0));
        }

        foreach (var (name, unique, partial) in indexDefinitions)
        {
            var indexColumns = new List<string>();
            using var columnCommand = connection.CreateCommand();
            columnCommand.CommandText = $"PRAGMA index_info({QuoteIdentifier(name)})";
            using var columnReader = columnCommand.ExecuteReader();
            while (columnReader.Read())
                if (!columnReader.IsDBNull(2))
                    indexColumns.Add(columnReader.GetString(2));
            result.Add(new(name, tableName, unique, partial, indexColumns));
        }
        return result;
    }

    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
