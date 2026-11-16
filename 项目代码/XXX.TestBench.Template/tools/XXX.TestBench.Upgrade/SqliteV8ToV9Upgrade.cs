using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XXX.TestBench.Infrastructure.Persistence;

namespace XXX.TestBench.Upgrade;

/// <summary>
/// 在独立副本中将经严格检查的 v8 SQLite 数据库升级为 v9。
/// </summary>
public sealed class SqliteV8ToV9Upgrade
{
    public async Task<UpgradeReport> CheckAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var report = new UpgradeReport { Command = "check-db-v8-to-v9", SourcePath = source };
        if (!File.Exists(source))
        {
            report.Blockers.Add("来源数据库不存在");
            report.FailureCode = 5;
            return report;
        }

        var before = await HashFileAsync(source, cancellationToken);
        report.SourceSha256 = before;
        SqliteDatabaseInspection inspection;
        try
        {
            inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(source, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Blockers.Add($"v8 只读结构检查失败：{ex.Message}");
            report.FailureCode = ex is IOException ? 5 : 4;
            return report;
        }

        report.SourceVersion = inspection.UserVersion;
        report.BeforeRowCounts.Clear();
        foreach (var table in SqliteSchemaV8.Tables)
        {
            if (inspection.Tables.Contains(table, StringComparer.OrdinalIgnoreCase))
                report.BeforeRowCounts[table] = await ReadCountAsync(source, table, cancellationToken);
        }

        foreach (var issue in SqliteSchemaV8.Validate(inspection))
            report.Blockers.Add(issue);
        if (inspection.UserVersion != SqliteSchemaV8.Version)
            report.Blockers.Add($"仅支持 v8，实际 user_version={inspection.UserVersion}");

        try
        {
            using var connection = OpenReadOnly(source);
            var runningRecords = ReadLegacyRows(connection, null,
                "SELECT id, record_number, started_at_utc FROM test_records WHERE state = 0 ORDER BY id");
            foreach (var row in runningRecords)
                report.Blockers.Add($"旧 Running 记录 id={row[0]}、record_number={row[1]}、started_at_utc={row[2]}；须先离线核查并明确中断处置，工具不会自动转换为可运行状态");

            AddInvalidValues(connection, report.Blockers, "test_records", "id", "state", "state NOT IN (0,1,2,3)");
            AddInvalidValues(connection, report.Blockers, "test_records", "id", "device_mode", "device_mode NOT IN (0,1,2)");
            AddInvalidValues(connection, report.Blockers, "test_item_results", "id", "state", "state NOT IN (0,1,2,3,4,5)");
            AddInvalidValues(connection, report.Blockers, "report_records", "id", "status", "status NOT IN (0,1,2)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Blockers.Add($"v8 记录状态读取失败：{ex.Message}");
        }

        var wal = source + "-wal";
        var shm = source + "-shm";
        if (File.Exists(wal) || File.Exists(shm))
            report.Blockers.Add($"检测到 SQLite WAL/SHM 侧车文件（WAL={File.Exists(wal)}，SHM={File.Exists(shm)}）；请正常关闭应用并确认数据库一致性后再升级");

        var after = await HashFileAsync(source, cancellationToken);
        if (!string.Equals(before, after, StringComparison.Ordinal))
            report.Blockers.Add("检查期间来源数据库发生变化；请关闭写入者后重试");

        report.Changes.Add("检查只读执行；不创建目录/数据库，不执行 checkpoint，不修改来源版本");
        report.Success = report.Blockers.Count == 0;
        report.FailureCode = report.Success ? 0 : 3;
        return report;
    }

    public async Task<UpgradeReport> ConvertAsync(
        string sourcePath,
        string outputPath,
        string? legacyBenchId,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var report = new UpgradeReport
        {
            Command = "convert-db-v8-to-v9",
            SourcePath = source,
            OutputPath = Path.IsPathFullyQualified(outputPath) ? Path.GetFullPath(outputPath) : outputPath
        };
        var check = await CheckAsync(source, cancellationToken);
        CopyReport(check, report);
        report.Command = "convert-db-v8-to-v9";
        report.OutputPath = Path.IsPathFullyQualified(outputPath) ? Path.GetFullPath(outputPath) : outputPath;
        if (!check.Success) return report;
        var hasLegacyRecords = report.BeforeRowCounts.TryGetValue("test_records", out var legacyRecordCount)
                               && legacyRecordCount > 0;
        if (hasLegacyRecords && string.IsNullOrWhiteSpace(legacyBenchId))
        {
            report.Blockers.Add("来源库包含历史试验记录；必须通过 --legacy-bench-id 明确指定其归属台架，工具不会从旧工位号推断台架身份");
            report.FailureCode = 2;
            return report;
        }
        var resolvedLegacyBenchId = legacyBenchId?.Trim() ?? string.Empty;
        if (!Path.IsPathFullyQualified(outputPath))
        {
            report.Blockers.Add("--output 必须是绝对路径");
            report.FailureCode = 2;
            return report;
        }

        var output = Path.GetFullPath(outputPath);
        var sourceHasDatabaseInstanceId = await HasDatabaseInstanceIdAsync(source, cancellationToken);
        var sourceHasStorageId = await HasStorageIdAsync(source, cancellationToken);
        if (PathsAlias(source, output))
        {
            report.Blockers.Add("来源与输出解析为同一文件，拒绝原地升级");
            report.FailureCode = 2;
            return report;
        }
        if (File.Exists(output) || Directory.Exists(output)
            || File.Exists(output + "-wal") || File.Exists(output + "-shm"))
        {
            report.Blockers.Add("输出数据库或其 WAL/SHM 侧车文件已存在，拒绝覆盖");
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
            sourceHash = await HashStreamAsync(sourceLock, cancellationToken);
            sourceLock.Position = 0;
            await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            report.OutputWritten = true;
            await sourceLock.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
        catch (IOException ex)
        {
            report.Blockers.Add($"来源仍被占用，或输出无法独占创建：{ex.Message}");
            report.FailureCode = 5;
            return report;
        }

        report.SourceSha256 = sourceHash;
        report.OutputSha256 = await HashFileAsync(output, cancellationToken);
        try
        {
            ConvertCopy(output, sourceHash, resolvedLegacyBenchId, report, cancellationToken);
            var sourceHashAfter = await HashFileAsync(source, cancellationToken);
            if (!string.Equals(sourceHash, sourceHashAfter, StringComparison.Ordinal))
                throw new InvalidOperationException("转换期间来源数据库发生变化，输出不可投入使用");

            var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(output, cancellationToken);
            var issues = SqliteSchemaV9.Validate(inspection);
            if (issues.Count > 0)
                throw new InvalidOperationException("重新打开输出后的 v9 结构检查失败：" + string.Join("；", issues));
            foreach (var table in SqliteSchemaV8.Tables)
            {
                var afterCount = await ReadCountAsync(output, table, cancellationToken);
                report.AfterRowCounts[table] = afterCount;
                if (!report.BeforeRowCounts.TryGetValue(table, out var beforeCount)) continue;
                var expectedCount = string.Equals(table, "app_metadata", StringComparison.OrdinalIgnoreCase)
                    ? beforeCount + (sourceHasDatabaseInstanceId ? 0 : 1) + (sourceHasStorageId ? 0 : 1)
                    : beforeCount;
                if (expectedCount != afterCount)
                    throw new InvalidOperationException($"{table} 行数变化：期望 {expectedCount}，实际 {afterCount}");
            }

            report.SourceSha256 = sourceHashAfter;
            report.OutputSha256 = await HashFileAsync(output, cancellationToken);
            report.OutputVersion = inspection.UserVersion;
            report.DatabaseInstanceId = inspection.AppMetadataDatabaseInstanceId;
            report.StorageId = inspection.AppMetadataStorageId;
            report.Changes.Add("已生成独立 v9 候选副本；来源数据库保持未修改");
            report.Changes.Add("历史记录按 Legacy 合同保留；RunId 由来源 SHA-256 与旧记录 ID 稳定派生，历史结论不重算");
            if (hasLegacyRecords)
                report.Changes.Add($"历史 BenchId 按显式参数登记为 {resolvedLegacyBenchId}；NodeId 保持为空");
            else
                report.Changes.Add("来源库没有试验记录；未推断或写入历史 BenchId");
            report.Success = true;
            report.FailureCode = 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.LegacyRunIdMappings.Clear();
            report.Blockers.Add($"副本升级失败，来源未修改；输出不得投入使用：{ex.Message}");
            report.Success = false;
            report.FailureCode = ex is IOException ? 5 : 3;
        }

        return report;
    }

    private static void ConvertCopy(
        string outputPath,
        string sourceSha256,
        string legacyBenchId,
        UpgradeReport report,
        CancellationToken cancellationToken)
    {
        using var connection = OpenReadWrite(outputPath);
        Execute(connection, null, "PRAGMA foreign_keys = OFF");
        using var transaction = connection.BeginTransaction();
        try
        {
            var statements = SqliteSchemaV9.CreateStatements;
            ExecuteAll(connection, transaction, statements.Where(sql =>
                sql.StartsWith("ALTER TABLE product_types ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("ALTER TABLE product_models ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("ALTER TABLE test_item_points ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("ALTER TABLE project_test_parameters ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("ALTER TABLE product_test_parameters ", StringComparison.OrdinalIgnoreCase)));

            RebuildRecords(connection, transaction, statements, sourceSha256, legacyBenchId, report, cancellationToken);
            RebuildItemResults(connection, transaction, statements, cancellationToken);
            RebuildReports(connection, transaction, statements, cancellationToken);

            ExecuteAll(connection, transaction, statements.Where(sql =>
                sql.StartsWith("ALTER TABLE audit_logs ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE TABLE model_resource_profiles ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE TABLE equipment_assets ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE TABLE maintenance_schedules ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE TABLE maintenance_records ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE TABLE alarm_events ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE TABLE bench_control_state ", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE UNIQUE INDEX ux_test_records_", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE UNIQUE INDEX ux_item_results_", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE UNIQUE INDEX ux_report_", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE UNIQUE INDEX ux_maintenance_", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE UNIQUE INDEX ux_alarm_", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE UNIQUE INDEX ux_equipment_", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE INDEX ix_records_", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE INDEX ix_reports_", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("CREATE INDEX ix_alarm_", StringComparison.OrdinalIgnoreCase)));

            Execute(connection, transaction,
                "UPDATE app_metadata SET value = '9' WHERE key = 'schema_version'");
            Execute(connection, transaction,
                "INSERT INTO app_metadata (key, value) VALUES ('database_instance_id', @value) ON CONFLICT(key) DO NOTHING",
                ("@value", DeterministicGuid("database:" + sourceSha256)));
            Execute(connection, transaction,
                "INSERT INTO app_metadata (key, value) VALUES ('storage_id', @value) ON CONFLICT(key) DO NOTHING",
                ("@value", DeterministicGuid("storage:" + sourceSha256)));
            Execute(connection, transaction, "PRAGMA user_version = 9");
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        finally
        {
            Execute(connection, null, "PRAGMA foreign_keys = ON");
        }

        var inspection = SqliteDatabaseInspector.InspectReadOnlyAsync(outputPath, cancellationToken).GetAwaiter().GetResult();
        foreach (var error in inspection.ForeignKeyErrors)
            report.Blockers.Add("外键检查失败：" + error);
        if (report.Blockers.Count > 0)
            throw new InvalidDataException("输出数据完整性未通过");
    }

    private static void RebuildRecords(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        IReadOnlyList<string> schemaStatements,
        string sourceSha256,
        string legacyBenchId,
        UpgradeReport report,
        CancellationToken cancellationToken)
    {
        var records = ReadLegacyRecords(connection, transaction);
        var create = FindCreate(schemaStatements, "test_records").Replace(
            "CREATE TABLE test_records ", "CREATE TABLE test_records_v9 ", StringComparison.OrdinalIgnoreCase);
        Execute(connection, transaction, create);
        const string insertSql = "INSERT INTO test_records_v9 (id, record_number, run_id, operation_id, start_request_id, product_model_id, product_number, vehicle_number, batch_number, station_number, remark, parameter_snapshot, sequence_snapshot, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc, device_configuration_revision, signal_bindings_snapshot, bench_id, node_id, result_contract_version, run_scope, previous_run_id, evaluation_state, evaluation_text, conclusion_source, execution_error, model_name_snapshot, operator_name_snapshot, resource_snapshot_json, precheck_snapshot_json, report_input_json, input_hash, execution_evidence_key, current_report_revision_id, row_version) VALUES (@id,@record_number,@run_id,NULL,NULL,@product_model_id,@product_number,NULL,@batch_number,@station_number,@remark,@parameter_snapshot,@sequence_snapshot,@device_mode,@operator_user_id,@state,@conclusion,@started_at_utc,@finished_at_utc,@device_configuration_revision,@signal_bindings_snapshot,@bench_id,NULL,1,NULL,NULL,5,NULL,'LegacyCode',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,0)";
        foreach (var row in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = DeterministicGuid($"run:{sourceSha256}:{row.Id.ToString(CultureInfo.InvariantCulture)}");
            report.LegacyRunIdMappings.Add(row.Id, runId);
            Execute(connection, transaction, insertSql,
                ("@id", row.Id), ("@record_number", row.RecordNumber),
                ("@run_id", runId),
                ("@product_model_id", row.ProductModelId), ("@product_number", row.ProductNumber),
                ("@batch_number", row.BatchNumber), ("@station_number", row.StationNumber), ("@remark", row.Remark),
                ("@parameter_snapshot", row.ParameterSnapshot), ("@sequence_snapshot", row.SequenceSnapshot),
                ("@device_mode", row.DeviceMode), ("@operator_user_id", row.OperatorUserId),
                ("@state", row.State), ("@conclusion", row.Conclusion), ("@started_at_utc", row.StartedAtUtc),
                ("@finished_at_utc", row.FinishedAtUtc), ("@device_configuration_revision", row.DeviceConfigurationRevision),
                ("@signal_bindings_snapshot", row.SignalBindingsSnapshot), ("@bench_id", legacyBenchId));
        }
        Execute(connection, transaction, "DROP TABLE test_records");
        Execute(connection, transaction, "ALTER TABLE test_records_v9 RENAME TO test_records");
    }

    private static void RebuildItemResults(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        IReadOnlyList<string> schemaStatements,
        CancellationToken cancellationToken)
    {
        var rows = ReadLegacyRows(connection, transaction,
            "SELECT id, record_id, test_item_point_id, state, summary_value, result_text, started_at_utc, finished_at_utc FROM test_item_results ORDER BY id");
        var create = FindCreate(schemaStatements, "test_item_results").Replace(
            "CREATE TABLE test_item_results ", "CREATE TABLE test_item_results_v9 ", StringComparison.OrdinalIgnoreCase);
        Execute(connection, transaction, create);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Execute(connection, transaction,
                "INSERT INTO test_item_results_v9 (id,record_id,test_item_point_id,state,summary_value,result_text,started_at_utc,finished_at_utc,measurements_json,execution_slot_key,row_version) VALUES (@id,@record_id,@point_id,@state,@summary,@result,@started,@finished,NULL,NULL,0)",
                ("@id", row[0]), ("@record_id", row[1]), ("@point_id", row[2]), ("@state", row[3]),
                ("@summary", row[4]), ("@result", row[5]), ("@started", row[6]), ("@finished", row[7]));
        }
        Execute(connection, transaction, "DROP TABLE test_item_results");
        Execute(connection, transaction, "ALTER TABLE test_item_results_v9 RENAME TO test_item_results");
    }

    private static void RebuildReports(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        IReadOnlyList<string> schemaStatements,
        CancellationToken cancellationToken)
    {
        var rows = ReadLegacyRows(connection, transaction,
            "SELECT id, test_record_id, template_path, output_path, status, error, created_by_user_id, created_at_utc, completed_at_utc FROM report_records ORDER BY id");
        var create = FindCreate(schemaStatements, "report_records").Replace(
            "CREATE TABLE report_records ", "CREATE TABLE report_records_v9 ", StringComparison.OrdinalIgnoreCase);
        Execute(connection, transaction, create);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Execute(connection, transaction,
                "INSERT INTO report_records_v9 (id,test_record_id,template_path,output_path,status,error,created_by_user_id,created_at_utc,completed_at_utc,revision_id,revision_no,trigger,owner_node_id,attempt_token,active_attempt_key,storage_key,template_hash,mapping_hash,input_hash,manifest_hash,engine_name,engine_version,calculation_result_json,error_stage,request_id,purpose,row_version,pdf_storage_key,pdf_artifact_id,pdf_hash,pdf_manifest_key,pdf_state) VALUES (@id,@record_id,@template,@output,@status,@error,@user_id,@created,@completed,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,0,NULL,NULL,NULL,NULL,0)",
                ("@id", row[0]), ("@record_id", row[1]), ("@template", row[2]), ("@output", row[3]),
                ("@status", row[4]), ("@error", row[5]), ("@user_id", row[6]),
                ("@created", row[7]), ("@completed", row[8]));
        }
        Execute(connection, transaction, "DROP TABLE report_records");
        Execute(connection, transaction, "ALTER TABLE report_records_v9 RENAME TO report_records");
    }

    private static List<LegacyRecord> ReadLegacyRecords(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        var result = new List<LegacyRecord>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,record_number,product_model_id,product_number,batch_number,station_number,remark,parameter_snapshot,sequence_snapshot,device_mode,operator_user_id,state,conclusion,started_at_utc,finished_at_utc,device_configuration_revision,signal_bindings_snapshot FROM test_records ORDER BY id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new LegacyRecord(
                reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), GetNullableString(reader, 3),
                GetNullableString(reader, 4), GetNullableString(reader, 5), GetNullableString(reader, 6),
                GetNullableString(reader, 7), GetNullableString(reader, 8), reader.GetInt32(9), reader.GetInt32(10),
                reader.GetInt32(11), GetNullableString(reader, 12), reader.GetString(13), GetNullableString(reader, 14),
                GetNullableString(reader, 15), GetNullableString(reader, 16)));
        }
        return result;
    }

    private static List<object?[]> ReadLegacyRows(SQLiteConnection connection, SQLiteTransaction? transaction, string sql)
    {
        var rows = new List<object?[]>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var values = new object?[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
                values[index] = reader.IsDBNull(index) ? DBNull.Value : reader.GetValue(index);
            rows.Add(values);
        }
        return rows;
    }

    private static string FindCreate(IReadOnlyList<string> statements, string table)
        => statements.Single(sql => sql.StartsWith($"CREATE TABLE {table} ", StringComparison.OrdinalIgnoreCase));

    private static void ExecuteAll(SQLiteConnection connection, SQLiteTransaction transaction, IEnumerable<string> sqlStatements)
    {
        foreach (var sql in sqlStatements)
            Execute(connection, transaction, sql);
    }

    private static void Execute(SQLiteConnection connection, SQLiteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SQLiteException ex)
        {
            throw new InvalidOperationException($"执行升级 SQL 失败：{sql}", ex);
        }
    }

    private static object? Scalar(SQLiteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void AddInvalidValues(
        SQLiteConnection connection,
        ICollection<string> blockers,
        string table,
        string idColumn,
        string valueColumn,
        string condition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"{idColumn}\", \"{valueColumn}\" FROM \"{table}\" WHERE {condition} ORDER BY \"{idColumn}\"";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            blockers.Add($"{table} id={reader.GetValue(0)} 的 {valueColumn}={reader.GetValue(1)} 是未知/非法枚举值");
    }

    private static string? GetNullableString(SQLiteDataReader reader, int index)
        => reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);

    private static string DeterministicGuid(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString("D").ToLowerInvariant();
    }

    private static SQLiteConnection OpenReadOnly(string path)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path), ReadOnly = true, Pooling = false, ForeignKeys = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static SQLiteConnection OpenReadWrite(string path)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path), Pooling = false, ForeignKeys = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static Task<long> ReadCountAsync(string path, string table, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenReadOnly(path);
        var count = Convert.ToInt64(Scalar(connection, $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\""), CultureInfo.InvariantCulture);
        return Task.FromResult(count);
    }

    private static Task<bool> HasDatabaseInstanceIdAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenReadOnly(path);
        var exists = Scalar(connection, "SELECT 1 FROM app_metadata WHERE key = 'database_instance_id' LIMIT 1") is not null;
        return Task.FromResult(exists);
    }

    private static Task<bool> HasStorageIdAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenReadOnly(path);
        var exists = Scalar(connection, "SELECT 1 FROM app_metadata WHERE key = 'storage_id' LIMIT 1") is not null;
        return Task.FromResult(exists);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await HashStreamAsync(stream, cancellationToken);
    }

    private static async Task<string> HashStreamAsync(Stream stream, CancellationToken cancellationToken)
        => Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();

    private static bool PathsAlias(string source, string output)
        => string.Equals(ResolveExistingPath(source), ResolveExistingPath(output), StringComparison.OrdinalIgnoreCase);

    private static string ResolveExistingPath(string path)
    {
        var full = Path.GetFullPath(path);
        var file = new FileInfo(full);
        if (file.Exists && file.ResolveLinkTarget(true) is { } resolvedFile)
            return Path.GetFullPath(resolvedFile.FullName);
        var parent = Path.GetDirectoryName(full);
        if (parent is null) return full;
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
        target.DatabaseInstanceId = source.DatabaseInstanceId;
        target.StorageId = source.StorageId;
        target.OutputWritten = source.OutputWritten;
        foreach (var item in source.BeforeRowCounts) target.BeforeRowCounts[item.Key] = item.Value;
        foreach (var item in source.LegacyRunIdMappings) target.LegacyRunIdMappings[item.Key] = item.Value;
        target.Changes.AddRange(source.Changes);
        target.Warnings.AddRange(source.Warnings);
        target.Blockers.AddRange(source.Blockers);
    }

    private sealed record LegacyRecord(
        int Id, string RecordNumber, int ProductModelId, string? ProductNumber, string? BatchNumber,
        string? StationNumber, string? Remark, string? ParameterSnapshot, string? SequenceSnapshot,
        int DeviceMode, int OperatorUserId, int State, string? Conclusion, string StartedAtUtc,
        string? FinishedAtUtc, string? DeviceConfigurationRevision, string? SignalBindingsSnapshot);
}
