using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Infrastructure.Persistence;

namespace XXX.TestBench.Upgrade;

/// <summary>
/// 将经预检的 v7 数据库转换为一个全新 v8 副本，永不原地修改来源。
/// </summary>
public sealed class SqliteV7ToV8Upgrade
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> V7Columns = BuildV7Columns();

    public async Task<UpgradeReport> CheckAsync(string sourcePath, CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var report = new UpgradeReport { Command = "check-db", SourcePath = source };
        if (!File.Exists(source))
        {
            report.Blockers.Add("来源数据库不存在");
            report.FailureCode = 5;
            return report;
        }

        var hashBefore = await HashFileAsync(source, ct);
        report.SourceSha256 = hashBefore;
        SqliteDatabaseInspection inspection;
        try
        {
            inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(source, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            report.Blockers.Add($"只读 SQLite 结构/完整性探测失败：{ex.Message}");
            report.FailureCode = ex is IOException ? 5 : 4;
            report.SourceSha256 = await HashFileAsync(source, ct);
            return report;
        }
        report.SourceVersion = inspection.UserVersion;
        foreach (var issue in ValidateV7(inspection))
            report.Blockers.Add(issue);
        if (!string.Equals(inspection.IntegrityCheck, "ok", StringComparison.OrdinalIgnoreCase)
            || inspection.ForeignKeyErrors.Count > 0)
            report.FailureCode = 4;
        else if (inspection.UserVersion != 7
                 || inspection.AppMetadataSchemaVersion is not ("1" or "7"))
            report.FailureCode = 2;
        else if (report.Blockers.Count > 0)
            report.FailureCode = report.Blockers.Any(message => message.Contains("扩展对象", StringComparison.OrdinalIgnoreCase)) ? 3 : 2;
        try
        {
            using var connection = OpenReadOnly(source);
            var running = Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM test_records WHERE state = 0"), CultureInfo.InvariantCulture);
            if (running > 0)
            {
                report.Blockers.Add($"存在 {running} 条 Running 记录；工具不结束或删除活动记录");
                report.FailureCode = 3;
            }
            ValidatePermissionRows(connection);
            if (Scalar(connection, "SELECT id FROM roles WHERE system_key = 'Administrator' LIMIT 1") is null)
            {
                report.Blockers.Add("缺少 system_key=Administrator，不能按系统角色规则升级");
                report.FailureCode = 3;
            }
        }
        catch (Exception ex)
        {
            report.Blockers.Add($"读取 v7 运行状态或权限失败：{ex.Message}");
            report.FailureCode = ex is IOException ? 5 : 3;
        }
        if (inspection.AppMetadataSchemaVersion == "1")
            report.Warnings.Add("app_metadata.schema_version 仍为 1；user_version 与 v7 结构符合时按已知历史行为接受，升级后同步写为 8");

        foreach (var table in V7Columns.Keys)
        {
            if (!inspection.Tables.Contains(table, StringComparer.OrdinalIgnoreCase))
                continue;
            try
            {
                report.BeforeRowCounts[table] = await ReadCountAsync(source, table, ct);
            }
            catch (Exception ex)
            {
                report.Blockers.Add($"读取 {table} 行数失败：{ex.Message}");
                report.FailureCode = 5;
            }
        }

        var wal = source + "-wal";
        var shm = source + "-shm";
        if (File.Exists(wal) || File.Exists(shm))
        {
            report.Blockers.Add($"检测到 SQLite WAL/SHM 侧车文件（WAL={File.Exists(wal)}，SHM={File.Exists(shm)}）；请正常关闭应用并确认 WAL 已合并后再检查/转换");
            report.FailureCode = 5;
        }

        var hashAfter = await HashFileAsync(source, ct);
        if (!string.Equals(hashBefore, hashAfter, StringComparison.Ordinal))
        {
            report.Blockers.Add("检查期间来源文件发生变化，可能仍有写入者；请关闭应用后重新检查");
            report.FailureCode = 5;
        }

        report.Changes.Add("check-db 仅只读检查，不创建副本、不执行 checkpoint、不修改 user_version");
        report.Success = report.Blockers.Count == 0;
        if (report.Success)
            report.FailureCode = 0;
        return report;
    }

    public async Task<UpgradeReport> ConvertAsync(string sourcePath, string outputPath, CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var output = Path.IsPathFullyQualified(outputPath) ? Path.GetFullPath(outputPath) : outputPath;
        var report = new UpgradeReport { Command = "convert-db", SourcePath = source, OutputPath = output };
        var check = await CheckAsync(source, ct);
        CopyReport(check, report);
        report.OutputPath = output;
        report.Command = "convert-db";
        if (!check.Success)
            return report;
        if (!Path.IsPathFullyQualified(outputPath))
        {
            report.Blockers.Add("--output 必须是绝对路径");
            report.FailureCode = 2;
            return report;
        }
        if (PathsAlias(source, output))
        {
            report.Blockers.Add("来源与输出解析为同一文件，拒绝原地升级");
            report.FailureCode = 2;
            return report;
        }
        if (File.Exists(output) || Directory.Exists(output))
        {
            report.Blockers.Add("输出路径已存在，拒绝覆盖");
            report.FailureCode = 2;
            return report;
        }

        var outputDirectory = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            report.Blockers.Add("无法确定输出目录");
            report.FailureCode = 2;
            return report;
        }
        Directory.CreateDirectory(outputDirectory);

        string sourceHash;
        try
        {
            using var sourceLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
            sourceHash = await HashStreamAsync(sourceLock, ct);
            sourceLock.Position = 0;
            using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            report.OutputWritten = true;
            await sourceLock.CopyToAsync(destination, ct);
            await destination.FlushAsync(ct);
        }
        catch (IOException ex)
        {
            report.Blockers.Add($"来源仍被占用，或输出无法独占创建：{ex.Message}");
            report.FailureCode = 5;
            return report;
        }

        report.SourceSha256 = sourceHash;
        report.OutputSha256 = await HashFileAsync(output, ct);
        report.Changes.Add("已通过 CreateNew 建立独立数据库副本，来源文件保持只读");
        try
        {
            ConvertCopy(output, report, ct);
            report.SourceSha256 = await HashFileAsync(source, ct);
            if (!string.Equals(sourceHash, report.SourceSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("升级期间来源文件哈希变化，转换结果不可用");

            report.OutputSha256 = await HashFileAsync(output, ct);
            var outputInspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(output, ct);
            var outputIssues = SqliteSchemaV8.Validate(outputInspection);
            if (outputIssues.Count > 0)
                throw new InvalidOperationException("重新打开输出库后的 v8 校验失败：" + string.Join("；", outputIssues));
            report.OutputVersion = outputInspection.UserVersion;
            report.Success = true;
            report.FailureCode = 0;
            return report;
        }
        catch (Exception ex)
        {
            report.Blockers.Add($"副本升级失败，来源未修改，输出不得投入使用：{ex.Message}");
            report.Success = false;
            report.FailureCode = ex is InvalidDataException ? 4 : ex is IOException ? 5 : 3;
            return report;
        }
    }

    private static IReadOnlyList<string> ValidateV7(SqliteDatabaseInspection inspection)
    {
        var issues = new List<string>();
        if (!inspection.Exists)
            return new[] { "来源数据库不存在" };
        if (inspection.UserVersion != 7)
            issues.Add($"仅支持 SQLite v7，实际 user_version={inspection.UserVersion}");
        if (!string.Equals(inspection.IntegrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
            issues.Add($"SQLite integrity_check 失败：{inspection.IntegrityCheck}");
        issues.AddRange(inspection.ForeignKeyErrors.Select(item => $"foreign_key_check：{item}"));
        if (inspection.AppMetadataSchemaVersion is not ("1" or "7"))
            issues.Add($"app_metadata.schema_version 不是已知 v7 值（实际 {inspection.AppMetadataSchemaVersion ?? "缺失"}）");

        var actualTables = inspection.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedTables = V7Columns.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        issues.AddRange(expectedTables.Except(actualTables, StringComparer.OrdinalIgnoreCase).Select(name => $"v7 缺少表 {name}"));
        issues.AddRange(actualTables.Except(expectedTables, StringComparer.OrdinalIgnoreCase).Select(name => $"v7 存在未知表 {name}"));
        foreach (var (table, expectedColumns) in V7Columns)
        {
            if (!inspection.Columns.TryGetValue(table, out var actualColumns))
                continue;
            var actual = actualColumns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expected = expectedColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            issues.AddRange(expected.Except(actual, StringComparer.OrdinalIgnoreCase).Select(name => $"{table} 缺少列 {name}"));
            issues.AddRange(actual.Except(expected, StringComparer.OrdinalIgnoreCase).Select(name => $"{table} 存在未知列 {name}"));
        }

        var expectedPrimaryKeys = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_metadata"] = new[] { "key" }, ["roles"] = new[] { "id" },
            ["role_permissions"] = new[] { "role_id", "permission_code" }, ["users"] = new[] { "id" },
            ["product_types"] = new[] { "id" }, ["product_models"] = new[] { "id" },
            ["test_item_points"] = new[] { "id" }, ["model_point_configs"] = new[] { "id" },
            ["project_test_parameters"] = new[] { "scope_key" }, ["product_test_parameters"] = new[] { "product_model_id" },
            ["test_records"] = new[] { "id" }, ["test_item_results"] = new[] { "id" },
            ["report_records"] = new[] { "id" }, ["audit_logs"] = new[] { "id" }
        };
        foreach (var (table, expected) in expectedPrimaryKeys)
        {
            if (!inspection.Columns.TryGetValue(table, out var columns))
                continue;
            var actual = columns.Where(column => column.IsPrimaryKey).Select(column => column.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actual.SetEquals(expected))
                issues.Add($"v7 表 {table} 主键结构不符");
        }

        var expectedForeignKeys = new[]
        {
            "role_permissions.role_id->roles.id", "users.role_id->roles.id",
            "product_models.product_type_id->product_types.id", "test_item_points.product_type_id->product_types.id",
            "model_point_configs.product_model_id->product_models.id", "model_point_configs.test_item_point_id->test_item_points.id",
            "product_test_parameters.product_type_id->product_types.id", "product_test_parameters.product_model_id->product_models.id",
            "test_records.product_model_id->product_models.id", "test_records.operator_user_id->users.id",
            "test_item_results.record_id->test_records.id", "report_records.test_record_id->test_records.id",
            "report_records.created_by_user_id->users.id"
        }.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualForeignKeys = inspection.ForeignKeys
            .Select(key => $"{key.FromTable}.{key.FromColumn}->{key.Table}.{key.ToColumn}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        issues.AddRange(expectedForeignKeys.Except(actualForeignKeys, StringComparer.OrdinalIgnoreCase).Select(item => $"v7 缺少外键 {item}"));
        issues.AddRange(actualForeignKeys.Except(expectedForeignKeys, StringComparer.OrdinalIgnoreCase).Select(item => $"v7 存在合同外外键 {item}"));

        bool HasUniqueIndex(string table, params string[] columns)
            => inspection.Indexes.Any(index => index.Table.Equals(table, StringComparison.OrdinalIgnoreCase)
                && index.IsUnique && index.Columns.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase));
        foreach (var (table, columns) in new[]
                 {
                     ("roles", new[] { "name" }), ("role_permissions", new[] { "role_id", "permission_code" }),
                     ("users", new[] { "login_name" }), ("model_point_configs", new[] { "product_model_id", "test_item_point_id" }),
                     ("test_records", new[] { "record_number" }), ("product_types", new[] { "code" }),
                     ("product_models", new[] { "product_type_id", "code" }),
                     ("test_item_points", new[] { "product_type_id", "code" })
                 })
            if (!HasUniqueIndex(table, columns))
                issues.Add($"v7 表 {table} 缺少唯一约束 ({string.Join(",", columns)})");
        if (!inspection.Indexes.Any(index => index.Table.Equals("roles", StringComparison.OrdinalIgnoreCase)
                && index.Name.Equals("ux_roles_system_key", StringComparison.OrdinalIgnoreCase)
                && index.IsUnique && index.IsPartial
                && index.Columns.SequenceEqual(new[] { "system_key" }, StringComparer.OrdinalIgnoreCase)))
            issues.Add("v7 缺少唯一的 ux_roles_system_key 部分索引");

        foreach (var item in inspection.Objects)
        {
            if (item.Type.Equals("table", StringComparison.OrdinalIgnoreCase))
                continue;
            if (item.Type.Equals("index", StringComparison.OrdinalIgnoreCase)
                && item.Name.Equals("ux_roles_system_key", StringComparison.OrdinalIgnoreCase))
                continue;
            if (item.Type.Equals("index", StringComparison.OrdinalIgnoreCase)
                && item.Name.StartsWith("sqlite_autoindex_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (item.Type.Equals("index", StringComparison.OrdinalIgnoreCase)
                && !new[] { "product_types", "product_models", "test_item_points" }.Contains(item.Table, StringComparer.OrdinalIgnoreCase))
                continue;
            issues.Add($"存在需人工检查的 v7 扩展对象 {item.Type}:{item.Name}（表 {item.Table}）");
        }

        return issues;
    }

    private static void ConvertCopy(string output, UpgradeReport report, CancellationToken ct)
    {
        using var connection = OpenReadWrite(output);
        Execute(connection, null, "PRAGMA foreign_keys = OFF");
        if (Convert.ToInt32(Scalar(connection, "PRAGMA foreign_keys"), CultureInfo.InvariantCulture) != 0)
            throw new InvalidOperationException("无法在迁移连接上关闭 foreign_keys");

        try
        {
            ct.ThrowIfCancellationRequested();
            var sequence = ReadSequences(connection);
            var beforeDigests = ReadAllDigests(connection);
            var permissionsBefore = ReadPermissions(connection, null);
            var runningCount = Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM test_records WHERE state = 0"), CultureInfo.InvariantCulture);
            if (runningCount > 0)
                throw new InvalidOperationException($"存在 {runningCount} 条 Running 记录；工具不结束或删除活动记录");
            ValidatePermissionRows(connection);

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    CreateV8ReplacementTable(connection, transaction, "product_types");
                    CreateV8ReplacementTable(connection, transaction, "product_models");
                    CreateV8ReplacementTable(connection, transaction, "test_item_points");
                    Execute(connection, transaction, "INSERT INTO product_types_v8 (id, name, is_enabled, created_at_utc) SELECT id, name, is_enabled, created_at_utc FROM product_types");
                    Execute(connection, transaction, "INSERT INTO product_models_v8 (id, product_type_id, name, is_enabled, created_at_utc) SELECT id, product_type_id, name, is_enabled, created_at_utc FROM product_models");
                    Execute(connection, transaction, "INSERT INTO test_item_points_v8 (id, product_type_id, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc, updated_at_utc) SELECT id, product_type_id, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc, updated_at_utc FROM test_item_points");
                    Execute(connection, transaction, "DROP TABLE product_models");
                    Execute(connection, transaction, "DROP TABLE product_types");
                    Execute(connection, transaction, "DROP TABLE test_item_points");
                    Execute(connection, transaction, "ALTER TABLE product_types_v8 RENAME TO product_types");
                    Execute(connection, transaction, "ALTER TABLE product_models_v8 RENAME TO product_models");
                    Execute(connection, transaction, "ALTER TABLE test_item_points_v8 RENAME TO test_item_points");
                    RestoreSequences(connection, transaction, sequence);

                    var removedPermissionCount = Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM role_permissions WHERE permission_code = 7", transaction), CultureInfo.InvariantCulture);
                    Execute(connection, transaction, "DELETE FROM role_permissions WHERE permission_code = 7");
                    var adminIdValue = Scalar(connection, "SELECT id FROM roles WHERE system_key = 'Administrator'", transaction)
                        ?? throw new InvalidOperationException("缺少 system_key=Administrator，不能按系统角色规则补齐权限");
                    var adminId = Convert.ToInt64(adminIdValue, CultureInfo.InvariantCulture);
                    var adminPermissionsBefore = Convert.ToInt64(Scalar(connection,
                        "SELECT COUNT(*) FROM role_permissions WHERE role_id = @roleId", transaction, ("@roleId", adminId)), CultureInfo.InvariantCulture);
                    foreach (var permission in PermissionCatalog.ActiveCodes)
                        Execute(connection, transaction,
                            "INSERT OR IGNORE INTO role_permissions (role_id, permission_code) VALUES (@roleId, @permissionCode)",
                            ("@roleId", adminId), ("@permissionCode", (int)permission));
                    var adminPermissionsAfter = Convert.ToInt64(Scalar(connection,
                        "SELECT COUNT(*) FROM role_permissions WHERE role_id = @roleId", transaction, ("@roleId", adminId)), CultureInfo.InvariantCulture);
                    Execute(connection, transaction, "UPDATE app_metadata SET value = '8' WHERE key = 'schema_version'");
                    if (Convert.ToInt64(Scalar(connection, "SELECT changes()", transaction), CultureInfo.InvariantCulture) != 1)
                        throw new InvalidOperationException("app_metadata.schema_version 更新行数不是 1");
                    Execute(connection, transaction, "PRAGMA user_version = 8");

                    var actualDigests = ReadAllDigests(connection, transaction);
                    CompareDigests(beforeDigests, actualDigests);
                    ValidatePermissionTransformation(permissionsBefore, ReadPermissions(connection, transaction), adminId);
                    AssertNoForeignKeyErrors(connection, transaction);
                    transaction.Commit();

                    report.Changes.Add("product_types、product_models、test_item_points 重建为无 code 的 v8 结构，保留 ID 和业务字段");
                    report.Changes.Add($"移除废弃 permission_code=7：{removedPermissionCount} 条授权；管理员有效权限补齐：{Math.Max(0, (int)(adminPermissionsAfter - adminPermissionsBefore))} 条");
                    report.Changes.Add("PRAGMA user_version 与 app_metadata.schema_version 已在事务内更新为 8");
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        finally
        {
            Execute(connection, null, "PRAGMA foreign_keys = ON");
            if (Convert.ToInt32(Scalar(connection, "PRAGMA foreign_keys"), CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("迁移后无法恢复 foreign_keys=ON");
        }

        AssertNoForeignKeyErrors(connection, null);
        if (!string.Equals(Convert.ToString(Scalar(connection, "PRAGMA integrity_check"), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("升级后 integrity_check 未通过");

        foreach (var table in SqliteSchemaV8.Tables)
            report.AfterRowCounts[table] = Convert.ToInt64(Scalar(connection, $"SELECT COUNT(*) FROM {Quote(table)}"), CultureInfo.InvariantCulture);
    }

    private static void ValidatePermissionRows(SQLiteConnection connection)
    {
        var known = PermissionCatalog.ActiveCodes.Select(code => (int)code).Append(7).ToHashSet();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT permission_code FROM role_permissions";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = reader.GetInt32(0);
            if (!known.Contains(value))
                throw new InvalidOperationException($"检测到未知 permission_code={value}；本次只允许清理已知废弃值 7");
        }
    }

    private static HashSet<(long RoleId, int PermissionCode)> ReadPermissions(
        SQLiteConnection connection,
        SQLiteTransaction? transaction)
    {
        var values = new HashSet<(long, int)>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT role_id, permission_code FROM role_permissions";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            values.Add((reader.GetInt64(0), reader.GetInt32(1)));
        return values;
    }

    private static void ValidatePermissionTransformation(
        IReadOnlySet<(long RoleId, int PermissionCode)> before,
        IReadOnlySet<(long RoleId, int PermissionCode)> after,
        long administratorRoleId)
    {
        var expected = before.Where(pair => pair.PermissionCode != 7).ToHashSet();
        foreach (var permission in PermissionCatalog.ActiveCodes)
            expected.Add((administratorRoleId, (int)permission));
        if (!expected.SetEquals(after))
            throw new InvalidOperationException("role_permissions 转换超出权限 7 清理及 Administrator 有效权限补齐范围");
    }

    private static void CreateV8ReplacementTable(SQLiteConnection connection, SQLiteTransaction transaction, string table)
    {
        var statement = SqliteSchemaV8.CreateStatements.Single(sql => sql.StartsWith($"CREATE TABLE {table} ", StringComparison.Ordinal));
        statement = statement.Replace($"CREATE TABLE {table} ", $"CREATE TABLE {table}_v8 ", StringComparison.Ordinal);
        Execute(connection, transaction, statement);
    }

    private static Dictionary<string, long?> ReadSequences(SQLiteConnection connection)
    {
        var values = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in new[] { "product_types", "product_models", "test_item_points" })
        {
            var value = Scalar(connection, "SELECT seq FROM sqlite_sequence WHERE name = @name", null, ("@name", table));
            values[table] = value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        return values;
    }

    private static void RestoreSequences(SQLiteConnection connection, SQLiteTransaction transaction, IReadOnlyDictionary<string, long?> sequences)
    {
        foreach (var table in new[] { "product_types", "product_models", "test_item_points" })
        {
            var maxId = Convert.ToInt64(Scalar(connection, $"SELECT COALESCE(MAX(id), 0) FROM {Quote(table)}", transaction), CultureInfo.InvariantCulture);
            var old = sequences[table] ?? 0;
            var highWater = Math.Max(old, maxId);
            Execute(connection, transaction, "DELETE FROM sqlite_sequence WHERE name = @name", ("@name", table));
            if (highWater > 0)
                Execute(connection, transaction, "INSERT INTO sqlite_sequence (name, seq) VALUES (@name, @seq)", ("@name", table), ("@seq", highWater));
        }
    }

    private static Dictionary<string, (long Count, string Hash)> ReadAllDigests(SQLiteConnection connection, SQLiteTransaction? transaction = null)
    {
        var result = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, v7Columns) in V7Columns)
        {
            if (table is "app_metadata" or "role_permissions")
                continue;
            var columns = table is "product_types" or "product_models" or "test_item_points"
                ? v7Columns.Where(column => !column.Equals("code", StringComparison.OrdinalIgnoreCase)).ToArray()
                : v7Columns.ToArray();
            result[table] = ReadDigest(connection, transaction, table, columns);
        }
        return result;
    }

    private static (long Count, string Hash) ReadDigest(SQLiteConnection connection, SQLiteTransaction? transaction, string table, IReadOnlyList<string> columns)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(Quote))} FROM {Quote(table)} ORDER BY {string.Join(", ", columns.Where(column => column.Equals("id", StringComparison.OrdinalIgnoreCase) || column.EndsWith("_id", StringComparison.OrdinalIgnoreCase) || column.Equals("scope_key", StringComparison.OrdinalIgnoreCase)).Select(Quote))}";
        using var reader = command.ExecuteReader();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long count = 0;
        while (reader.Read())
        {
            count++;
            foreach (var value in Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)))
            {
                var bytes = CanonicalBytes(value);
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }
        }
        return (count, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static byte[] CanonicalBytes(object? value)
    {
        var text = value switch
        {
            null or DBNull => "NULL:",
            byte[] bytes => "BLOB:" + Convert.ToBase64String(bytes),
            double number => "REAL:" + number.ToString("R", CultureInfo.InvariantCulture),
            float number => "REAL:" + number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => value.GetType().Name + ":" + formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.GetType().Name + ":" + value
        };
        return Encoding.UTF8.GetBytes(text);
    }

    private static void CompareDigests(
        IReadOnlyDictionary<string, (long Count, string Hash)> expected,
        IReadOnlyDictionary<string, (long Count, string Hash)> actual)
    {
        foreach (var (table, before) in expected)
        {
            if (!actual.TryGetValue(table, out var after) || before.Count != after.Count || !string.Equals(before.Hash, after.Hash, StringComparison.Ordinal))
                throw new InvalidOperationException($"表 {table} 的行数或保留字段摘要不一致，事务已回滚");
        }
    }

    private static void AssertNoForeignKeyErrors(SQLiteConnection connection, SQLiteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check";
        using var reader = command.ExecuteReader();
        if (reader.Read())
            throw new InvalidOperationException($"foreign_key_check 失败：table={reader.GetValue(0)}, rowid={reader.GetValue(1)}, parent={reader.GetValue(2)}, fkid={reader.GetValue(3)}");
    }

    private static SQLiteConnection OpenReadOnly(string inspectionPath)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = inspectionPath, ReadOnly = true, Pooling = false, ForeignKeys = false }.ToString());
        connection.Open();
        return connection;
    }

    private static SQLiteConnection OpenReadWrite(string path)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = false }.ToString());
        connection.Open();
        return connection;
    }

    private static Task<long> ReadCountAsync(string path, string table, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = OpenReadOnly(path);
        return Task.FromResult(Convert.ToInt64(Scalar(connection, $"SELECT COUNT(*) FROM {Quote(table)}"), CultureInfo.InvariantCulture));
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await HashStreamAsync(stream, ct);
    }

    private static async Task<string> HashStreamAsync(Stream stream, CancellationToken ct)
        => Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildV7Columns()
    {
        var columns = SqliteSchemaV8.Columns.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        columns["product_types"] = new[] { "id", "code", "name", "is_enabled", "created_at_utc" };
        columns["product_models"] = new[] { "id", "product_type_id", "code", "name", "is_enabled", "created_at_utc" };
        columns["test_item_points"] = new[] { "id", "product_type_id", "code", "name", "executor_code", "result_kind", "is_enabled", "sort_order", "created_at_utc", "updated_at_utc" };
        return columns;
    }

    private static void Execute(SQLiteConnection connection, SQLiteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SQLiteConnection connection, string sql, SQLiteTransaction? transaction = null, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteScalar();
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static bool PathsAlias(string source, string output)
        => string.Equals(ResolveExistingPath(source), ResolveExistingPath(output), StringComparison.OrdinalIgnoreCase);

    private static string ResolveExistingPath(string path)
    {
        var full = Path.GetFullPath(path);
        var file = new FileInfo(full);
        if (file.Exists && file.ResolveLinkTarget(true) is { } resolvedFile)
            return Path.GetFullPath(resolvedFile.FullName);
        var parent = Path.GetDirectoryName(full);
        if (parent is null)
            return full;
        var directory = new DirectoryInfo(parent);
        while (!directory.Exists && directory.Parent is not null)
            directory = directory.Parent;
        var resolvedParent = directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName;
        var relative = Path.GetRelativePath(directory.FullName, parent);
        return Path.GetFullPath(Path.Combine(resolvedParent, relative, Path.GetFileName(full)));
    }

    private static void CopyReport(UpgradeReport source, UpgradeReport target)
    {
        target.SourceSha256 = source.SourceSha256;
        target.FailureCode = source.FailureCode;
        target.SourceVersion = source.SourceVersion;
        target.OutputVersion = source.OutputVersion;
        target.OutputWritten = source.OutputWritten;
        target.BeforeRowCounts.Clear();
        foreach (var item in source.BeforeRowCounts)
            target.BeforeRowCounts[item.Key] = item.Value;
        target.Changes.AddRange(source.Changes);
        target.Warnings.AddRange(source.Warnings);
        target.Blockers.AddRange(source.Blockers);
    }
}
