namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// v9 数据库结构合同。v8 保留为只读的历史合同，升级只由离线工具在副本上执行。
/// </summary>
public static class SqliteSchemaV9
{
    public const int Version = 9;

    public static IReadOnlyList<string> Tables { get; } = BuildTables();
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Columns { get; } = BuildColumns();
    public static IReadOnlyList<string> CreateStatements { get; } = BuildCreateStatements();

    private static IReadOnlyList<string> BuildTables()
        => SqliteSchemaV8.Tables.Concat(new[]
        {
            "model_resource_profiles", "equipment_assets", "maintenance_schedules",
            "maintenance_records", "alarm_events", "bench_control_state"
        }).ToArray();

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildColumns()
    {
        var columns = SqliteSchemaV8.Columns.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);

        Add("product_types", "row_version");
        Add("product_models", "row_version", "sequence_revision");
        Add("test_item_points", "row_version");
        Add("project_test_parameters", "row_version");
        Add("product_test_parameters", "row_version");
        Add("test_records",
            "run_id", "operation_id", "start_request_id", "vehicle_number", "bench_id", "node_id",
            "result_contract_version", "run_scope", "previous_run_id", "evaluation_state", "evaluation_text",
            "conclusion_source", "execution_error", "model_name_snapshot", "operator_name_snapshot",
            "resource_snapshot_json", "precheck_snapshot_json", "report_input_json", "input_hash",
            "execution_evidence_key", "current_report_revision_id", "row_version");
        Add("test_item_results", "measurements_json", "execution_slot_key", "row_version");
        Add("report_records",
            "revision_id", "revision_no", "trigger", "owner_node_id", "attempt_token", "active_attempt_key",
            "storage_key", "template_hash", "mapping_hash", "input_hash", "manifest_hash", "engine_name",
            "engine_version", "calculation_result_json", "error_stage", "request_id", "purpose", "row_version",
            "pdf_storage_key", "pdf_artifact_id", "pdf_hash", "pdf_manifest_key", "pdf_state");
        Add("audit_logs", "bench_id", "node_id", "run_id");

        columns["model_resource_profiles"] = new[]
        {
            "product_model_id", "template_key", "mapping_key", "process_template_key", "process_mapping_key",
            "guidance_key", "revision", "updated_by", "updated_at_utc", "row_version"
        };
        columns["equipment_assets"] = new[]
        {
            "asset_id", "bench_id", "device_id", "asset_code", "name", "model", "serial_number", "enabled",
            "remark", "created_at_utc", "updated_at_utc", "row_version"
        };
        columns["maintenance_schedules"] = new[]
        {
            "id", "asset_id", "kind", "cycle_description", "due_date", "advance_days", "enabled",
            "created_at_utc", "updated_at_utc", "row_version"
        };
        columns["maintenance_records"] = new[]
        {
            "id", "schedule_id", "performed_date", "result", "certificate_storage_key", "next_due_date",
            "recorded_by_user_id", "recorded_at_utc", "remark", "request_id", "request_hash"
        };
        columns["alarm_events"] = new[]
        {
            "event_id", "bench_id", "rule_id", "device_id", "point_id", "severity", "active_key",
            "first_raised_at_utc", "last_raised_at_utc", "recovered_at_utc", "acknowledged_at_utc",
            "acknowledged_by_user_id", "message", "advice", "evidence_json", "row_version"
        };
        columns["bench_control_state"] = new[]
        {
            "bench_id", "owner_node_id", "operation_id", "phase", "requires_recovery", "recovery_reason",
            "row_version", "updated_at_utc", "last_safe_stop_json", "last_evidence_key"
        };

        return columns;

        void Add(string table, params string[] names)
            => columns[table] = columns[table].Concat(names).ToArray();
    }

    private static IReadOnlyList<string> BuildCreateStatements()
    {
        var statements = SqliteSchemaV8.CreateStatements
            .Where(sql => !sql.StartsWith("CREATE TABLE test_records ", StringComparison.OrdinalIgnoreCase)
                          && !sql.StartsWith("CREATE TABLE test_item_results ", StringComparison.OrdinalIgnoreCase)
                          && !sql.StartsWith("CREATE TABLE report_records ", StringComparison.OrdinalIgnoreCase))
            .ToList();

        statements.AddRange(new[]
        {
            "ALTER TABLE product_types ADD COLUMN row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0)",
            "ALTER TABLE product_models ADD COLUMN row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0)",
            "ALTER TABLE product_models ADD COLUMN sequence_revision INTEGER NOT NULL DEFAULT 0 CHECK (sequence_revision >= 0)",
            "ALTER TABLE test_item_points ADD COLUMN row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0)",
            "ALTER TABLE project_test_parameters ADD COLUMN row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0)",
            "ALTER TABLE product_test_parameters ADD COLUMN row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0)",
            "CREATE TABLE test_records (id INTEGER PRIMARY KEY AUTOINCREMENT, record_number TEXT NOT NULL UNIQUE, run_id TEXT NOT NULL, operation_id TEXT NULL, start_request_id TEXT NULL, product_model_id INTEGER NOT NULL, product_number TEXT NULL, vehicle_number TEXT NULL, batch_number TEXT NULL, station_number TEXT NULL, remark TEXT NULL, parameter_snapshot TEXT NULL, sequence_snapshot TEXT NULL, device_mode INTEGER NOT NULL CHECK (device_mode IN (0,1,2)), operator_user_id INTEGER NOT NULL, state INTEGER NOT NULL DEFAULT 0 CHECK (state IN (0,1,2,3,4)), conclusion TEXT NULL, started_at_utc TEXT NOT NULL, finished_at_utc TEXT NULL, device_configuration_revision TEXT NULL, signal_bindings_snapshot TEXT NULL, bench_id TEXT NULL, node_id TEXT NULL, result_contract_version INTEGER NOT NULL DEFAULT 1 CHECK (result_contract_version IN (1,2)), run_scope INTEGER NULL CHECK (run_scope IS NULL OR run_scope IN (0,1)), previous_run_id TEXT NULL, evaluation_state INTEGER NULL CHECK (evaluation_state IS NULL OR evaluation_state IN (0,1,2,3,4,5)), evaluation_text TEXT NULL, conclusion_source TEXT NULL, execution_error TEXT NULL, model_name_snapshot TEXT NULL, operator_name_snapshot TEXT NULL, resource_snapshot_json TEXT NULL, precheck_snapshot_json TEXT NULL, report_input_json TEXT NULL, input_hash TEXT NULL, execution_evidence_key TEXT NULL, current_report_revision_id TEXT NULL, row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0), FOREIGN KEY (product_model_id) REFERENCES product_models(id) ON DELETE RESTRICT, FOREIGN KEY (operator_user_id) REFERENCES users(id) ON DELETE RESTRICT, CHECK (result_contract_version = 1 OR (operation_id IS NOT NULL AND start_request_id IS NOT NULL AND bench_id IS NOT NULL AND node_id IS NOT NULL AND run_scope IS NOT NULL)))",
            "CREATE TABLE test_item_results (id INTEGER PRIMARY KEY AUTOINCREMENT, record_id INTEGER NOT NULL, test_item_point_id INTEGER NOT NULL, state INTEGER NOT NULL DEFAULT 0 CHECK (state IN (0,1,2,3,4,5,6)), summary_value TEXT NULL, result_text TEXT NULL, started_at_utc TEXT NULL, finished_at_utc TEXT NULL, measurements_json TEXT NULL, execution_slot_key TEXT NULL, row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0), FOREIGN KEY (record_id) REFERENCES test_records(id) ON DELETE RESTRICT)",
            "CREATE TABLE report_records (id INTEGER PRIMARY KEY AUTOINCREMENT, test_record_id INTEGER NOT NULL, template_path TEXT NOT NULL, output_path TEXT NULL, status INTEGER NOT NULL DEFAULT 0 CHECK (status IN (0,1,2,3)), error TEXT NULL, created_by_user_id INTEGER NOT NULL, created_at_utc TEXT NOT NULL, completed_at_utc TEXT NULL, revision_id TEXT NULL, revision_no INTEGER NULL, trigger TEXT NULL, owner_node_id TEXT NULL, attempt_token TEXT NULL, active_attempt_key TEXT NULL, storage_key TEXT NULL, template_hash TEXT NULL, mapping_hash TEXT NULL, input_hash TEXT NULL, manifest_hash TEXT NULL, engine_name TEXT NULL, engine_version TEXT NULL, calculation_result_json TEXT NULL, error_stage TEXT NULL, request_id TEXT NULL, purpose INTEGER NULL CHECK (purpose IS NULL OR purpose IN (0,1)), row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0), pdf_storage_key TEXT NULL, pdf_artifact_id TEXT NULL, pdf_hash TEXT NULL, pdf_manifest_key TEXT NULL, pdf_state INTEGER NOT NULL DEFAULT 0 CHECK (pdf_state IN (0,1,2,3,4)), FOREIGN KEY (test_record_id) REFERENCES test_records(id) ON DELETE RESTRICT, FOREIGN KEY (created_by_user_id) REFERENCES users(id) ON DELETE RESTRICT, CHECK (revision_no IS NULL OR revision_no > 0), CHECK (revision_id IS NULL OR (revision_no IS NOT NULL AND owner_node_id IS NOT NULL AND input_hash IS NOT NULL AND purpose IS NOT NULL AND request_id IS NOT NULL)), CHECK (revision_id IS NULL OR ((status IN (0,3) AND active_attempt_key IS NOT NULL) OR (status IN (1,2) AND active_attempt_key IS NULL))))",
            "ALTER TABLE audit_logs ADD COLUMN bench_id TEXT NULL",
            "ALTER TABLE audit_logs ADD COLUMN node_id TEXT NULL",
            "ALTER TABLE audit_logs ADD COLUMN run_id TEXT NULL",
            "CREATE TABLE model_resource_profiles (product_model_id INTEGER PRIMARY KEY, template_key TEXT NOT NULL, mapping_key TEXT NOT NULL, process_template_key TEXT NULL, process_mapping_key TEXT NULL, guidance_key TEXT NULL, revision INTEGER NOT NULL CHECK (revision > 0), updated_by TEXT NOT NULL, updated_at_utc TEXT NOT NULL, row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0), FOREIGN KEY (product_model_id) REFERENCES product_models(id) ON DELETE RESTRICT, CHECK ((process_template_key IS NULL AND process_mapping_key IS NULL) OR (process_template_key IS NOT NULL AND process_mapping_key IS NOT NULL)))",
            "CREATE TABLE equipment_assets (asset_id TEXT PRIMARY KEY, bench_id TEXT NOT NULL, device_id TEXT NULL, asset_code TEXT NOT NULL, name TEXT NOT NULL, model TEXT NULL, serial_number TEXT NULL, enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0,1)), remark TEXT NULL, created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL, row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0))",
            "CREATE TABLE maintenance_schedules (id INTEGER PRIMARY KEY AUTOINCREMENT, asset_id TEXT NOT NULL, kind INTEGER NOT NULL, cycle_description TEXT NOT NULL, due_date TEXT NOT NULL, advance_days INTEGER NOT NULL DEFAULT 0 CHECK (advance_days >= 0), enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0,1)), created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL, row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0), UNIQUE (asset_id, kind), FOREIGN KEY (asset_id) REFERENCES equipment_assets(asset_id) ON DELETE RESTRICT)",
            "CREATE TABLE maintenance_records (id INTEGER PRIMARY KEY AUTOINCREMENT, schedule_id INTEGER NOT NULL, performed_date TEXT NOT NULL, result TEXT NOT NULL, certificate_storage_key TEXT NULL, next_due_date TEXT NULL, recorded_by_user_id INTEGER NOT NULL, recorded_at_utc TEXT NOT NULL, remark TEXT NULL, request_id TEXT NULL, request_hash TEXT NULL, FOREIGN KEY (schedule_id) REFERENCES maintenance_schedules(id) ON DELETE RESTRICT, FOREIGN KEY (recorded_by_user_id) REFERENCES users(id) ON DELETE RESTRICT, CHECK ((request_id IS NULL AND request_hash IS NULL) OR (request_id IS NOT NULL AND request_hash IS NOT NULL)))",
            "CREATE TABLE alarm_events (event_id TEXT PRIMARY KEY, bench_id TEXT NOT NULL, rule_id TEXT NOT NULL, device_id TEXT NULL, point_id TEXT NULL, severity INTEGER NOT NULL, active_key TEXT NULL, first_raised_at_utc TEXT NOT NULL, last_raised_at_utc TEXT NOT NULL, recovered_at_utc TEXT NULL, acknowledged_at_utc TEXT NULL, acknowledged_by_user_id INTEGER NULL, message TEXT NOT NULL, advice TEXT NULL, evidence_json TEXT NULL, row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0), FOREIGN KEY (acknowledged_by_user_id) REFERENCES users(id) ON DELETE RESTRICT, CHECK ((recovered_at_utc IS NULL AND active_key IS NOT NULL) OR (recovered_at_utc IS NOT NULL AND active_key IS NULL)), CHECK ((acknowledged_at_utc IS NULL AND acknowledged_by_user_id IS NULL) OR (acknowledged_at_utc IS NOT NULL AND acknowledged_by_user_id IS NOT NULL)))",
            "CREATE TABLE bench_control_state (bench_id TEXT PRIMARY KEY, owner_node_id TEXT NOT NULL, operation_id TEXT NULL, phase INTEGER NOT NULL CHECK (phase BETWEEN 0 AND 8), requires_recovery INTEGER NOT NULL CHECK (requires_recovery IN (0,1)), recovery_reason TEXT NULL, row_version INTEGER NOT NULL DEFAULT 0 CHECK (row_version >= 0), updated_at_utc TEXT NOT NULL, last_safe_stop_json TEXT NULL, last_evidence_key TEXT NULL, CHECK ((operation_id IS NULL AND phase = 0 AND requires_recovery = 0) OR (operation_id IS NOT NULL AND phase BETWEEN 1 AND 8)), CHECK (phase <> 8 OR (requires_recovery = 1 AND recovery_reason IS NOT NULL)))",
            "CREATE UNIQUE INDEX ux_test_records_run_id ON test_records(run_id)",
            "CREATE UNIQUE INDEX ux_test_records_start_request ON test_records(start_request_id) WHERE start_request_id IS NOT NULL",
            "CREATE UNIQUE INDEX ux_test_records_bench_running ON test_records(bench_id) WHERE state = 0 AND bench_id IS NOT NULL",
            "CREATE UNIQUE INDEX ux_item_results_execution_slot ON test_item_results(execution_slot_key) WHERE execution_slot_key IS NOT NULL",
            "CREATE UNIQUE INDEX ux_report_revision_id ON report_records(revision_id) WHERE revision_id IS NOT NULL",
            "CREATE UNIQUE INDEX ux_report_record_revision_no ON report_records(test_record_id, revision_no) WHERE revision_no IS NOT NULL",
            "CREATE UNIQUE INDEX ux_report_active_attempt ON report_records(active_attempt_key) WHERE active_attempt_key IS NOT NULL",
            "CREATE UNIQUE INDEX ux_report_request ON report_records(request_id) WHERE request_id IS NOT NULL",
            "CREATE UNIQUE INDEX ux_maintenance_request ON maintenance_records(request_id) WHERE request_id IS NOT NULL",
            "CREATE UNIQUE INDEX ux_alarm_active ON alarm_events(active_key) WHERE active_key IS NOT NULL",
            "CREATE UNIQUE INDEX ux_equipment_asset_bench_code ON equipment_assets(bench_id, asset_code)",
            "CREATE INDEX ix_records_bench_time ON test_records(bench_id, started_at_utc DESC, id DESC)",
            "CREATE INDEX ix_records_time ON test_records(started_at_utc DESC, id DESC)",
            "CREATE INDEX ix_reports_owner_status ON report_records(owner_node_id, status, created_at_utc)",
            "CREATE INDEX ix_alarm_bench_time ON alarm_events(bench_id, first_raised_at_utc DESC, event_id DESC)"
        });

        return statements;
    }

    public static IReadOnlyList<string> Validate(SqliteDatabaseInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        var issues = new List<string>();
        if (inspection.UserVersion != Version)
            issues.Add($"user_version 应为 {Version}，实际为 {inspection.UserVersion}");
        if (!string.Equals(inspection.IntegrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
            issues.Add($"integrity_check 未通过：{inspection.IntegrityCheck}");
        if (inspection.ForeignKeyErrors.Count > 0)
            issues.AddRange(inspection.ForeignKeyErrors.Select(item => $"foreign_key_check：{item}"));

        var actualTables = inspection.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedTables = Tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        issues.AddRange(expectedTables.Except(actualTables, StringComparer.OrdinalIgnoreCase).Select(name => $"缺少表 {name}"));
        issues.AddRange(actualTables.Except(expectedTables, StringComparer.OrdinalIgnoreCase).Select(name => $"存在 v9 合同外的表 {name}"));

        var permittedIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ux_roles_system_key", "ux_test_records_run_id", "ux_test_records_start_request",
            "ux_test_records_bench_running", "ux_item_results_execution_slot", "ux_report_revision_id",
            "ux_report_record_revision_no", "ux_report_active_attempt", "ux_report_request",
            "ux_maintenance_request", "ux_alarm_active", "ux_equipment_asset_bench_code",
            "ix_records_bench_time", "ix_records_time", "ix_reports_owner_status", "ix_alarm_bench_time"
        };
        issues.AddRange(inspection.Objects.Where(item =>
                !item.Type.Equals("table", StringComparison.OrdinalIgnoreCase)
                && !(item.Type.Equals("index", StringComparison.OrdinalIgnoreCase) && permittedIndexes.Contains(item.Name)))
            .Select(item => $"存在未登记的 SQLite 对象 {item.Type}:{item.Name}"));

        foreach (var (table, expectedColumns) in Columns)
        {
            if (!inspection.Columns.TryGetValue(table, out var actualColumns)) continue;
            var actual = actualColumns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expected = expectedColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            issues.AddRange(expected.Except(actual, StringComparer.OrdinalIgnoreCase).Select(name => $"{table} 缺少列 {name}"));
            issues.AddRange(actual.Except(expected, StringComparer.OrdinalIgnoreCase).Select(name => $"{table} 存在合同外列 {name}"));
        }

        if (!string.Equals(inspection.AppMetadataSchemaVersion, Version.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            issues.Add($"app_metadata.schema_version 应为 {Version}，实际为 {inspection.AppMetadataSchemaVersion ?? "缺失"}");
        if (!Guid.TryParseExact(inspection.AppMetadataDatabaseInstanceId, "D", out var databaseInstanceId)
            || !string.Equals(inspection.AppMetadataDatabaseInstanceId,
                databaseInstanceId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal))
            issues.Add("app_metadata.database_instance_id 必须是固定的小写 GUID D 格式");

        var expectedPrimaryKeys = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_metadata"] = ["key"], ["roles"] = ["id"], ["role_permissions"] = ["role_id", "permission_code"],
            ["users"] = ["id"], ["product_types"] = ["id"], ["product_models"] = ["id"],
            ["test_item_points"] = ["id"], ["model_point_configs"] = ["id"],
            ["project_test_parameters"] = ["scope_key"], ["product_test_parameters"] = ["product_model_id"],
            ["test_records"] = ["id"], ["test_item_results"] = ["id"], ["report_records"] = ["id"],
            ["audit_logs"] = ["id"], ["model_resource_profiles"] = ["product_model_id"],
            ["equipment_assets"] = ["asset_id"], ["maintenance_schedules"] = ["id"],
            ["maintenance_records"] = ["id"], ["alarm_events"] = ["event_id"], ["bench_control_state"] = ["bench_id"]
        };
        foreach (var (table, expected) in expectedPrimaryKeys)
        {
            if (!inspection.Columns.TryGetValue(table, out var columns)) continue;
            var actual = columns.Where(column => column.IsPrimaryKey).Select(column => column.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actual.SetEquals(expected)) issues.Add($"{table} 主键结构不符");
        }

        var expectedForeignKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "role_permissions.role_id->roles.id", "users.role_id->roles.id",
            "product_models.product_type_id->product_types.id", "test_item_points.product_type_id->product_types.id",
            "model_point_configs.product_model_id->product_models.id", "model_point_configs.test_item_point_id->test_item_points.id",
            "product_test_parameters.product_type_id->product_types.id", "product_test_parameters.product_model_id->product_models.id",
            "test_records.product_model_id->product_models.id", "test_records.operator_user_id->users.id",
            "test_item_results.record_id->test_records.id", "report_records.test_record_id->test_records.id",
            "report_records.created_by_user_id->users.id", "model_resource_profiles.product_model_id->product_models.id",
            "maintenance_schedules.asset_id->equipment_assets.asset_id", "maintenance_records.schedule_id->maintenance_schedules.id",
            "maintenance_records.recorded_by_user_id->users.id", "alarm_events.acknowledged_by_user_id->users.id"
        };
        var actualForeignKeys = inspection.ForeignKeys
            .Select(key => $"{key.FromTable}.{key.FromColumn}->{key.Table}.{key.ToColumn}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        issues.AddRange(expectedForeignKeys.Except(actualForeignKeys, StringComparer.OrdinalIgnoreCase).Select(item => $"缺少外键 {item}"));
        issues.AddRange(actualForeignKeys.Except(expectedForeignKeys, StringComparer.OrdinalIgnoreCase).Select(item => $"存在合同外外键 {item}"));

        var expectedIndexes = new (string Name, string Table, bool Unique, bool Partial, string[] Columns)[]
        {
            ("ux_roles_system_key", "roles", true, true, ["system_key"]),
            ("ux_test_records_run_id", "test_records", true, false, ["run_id"]),
            ("ux_test_records_start_request", "test_records", true, true, ["start_request_id"]),
            ("ux_test_records_bench_running", "test_records", true, true, ["bench_id"]),
            ("ux_item_results_execution_slot", "test_item_results", true, true, ["execution_slot_key"]),
            ("ux_report_revision_id", "report_records", true, true, ["revision_id"]),
            ("ux_report_record_revision_no", "report_records", true, true, ["test_record_id", "revision_no"]),
            ("ux_report_active_attempt", "report_records", true, true, ["active_attempt_key"]),
            ("ux_report_request", "report_records", true, true, ["request_id"]),
            ("ux_maintenance_request", "maintenance_records", true, true, ["request_id"]),
            ("ux_alarm_active", "alarm_events", true, true, ["active_key"]),
            ("ux_equipment_asset_bench_code", "equipment_assets", true, false, ["bench_id", "asset_code"]),
            ("ix_records_bench_time", "test_records", false, false, ["bench_id", "started_at_utc", "id"]),
            ("ix_records_time", "test_records", false, false, ["started_at_utc", "id"]),
            ("ix_reports_owner_status", "report_records", false, false, ["owner_node_id", "status", "created_at_utc"]),
            ("ix_alarm_bench_time", "alarm_events", false, false, ["bench_id", "first_raised_at_utc", "event_id"])
        };
        foreach (var expected in expectedIndexes)
        {
            var index = inspection.Indexes.FirstOrDefault(item => item.Name.Equals(expected.Name, StringComparison.OrdinalIgnoreCase));
            if (index is null || !index.Table.Equals(expected.Table, StringComparison.OrdinalIgnoreCase)
                || index.IsUnique != expected.Unique || index.IsPartial != expected.Partial
                || !index.Columns.SequenceEqual(expected.Columns, StringComparer.OrdinalIgnoreCase))
                issues.Add($"缺少或错误的索引 {expected.Name}");
            // PRAGMA index_list 只能说明存在过滤条件，不能证明 WHERE 的范围正确。
            // 错误的过滤条件会绕过“一台一条 Running”等唯一约束，因此同时核对定义。
            if (expected.Partial)
            {
                var expectedSql = CreateStatements.First(sql => sql.StartsWith(
                    $"CREATE UNIQUE INDEX {expected.Name} ", StringComparison.OrdinalIgnoreCase));
                var actualSql = inspection.Objects.FirstOrDefault(item =>
                    item.Type.Equals("index", StringComparison.OrdinalIgnoreCase)
                    && item.Name.Equals(expected.Name, StringComparison.OrdinalIgnoreCase))?.Sql;
                if (!string.Equals(IndexPredicate(expectedSql), IndexPredicate(actualSql), StringComparison.OrdinalIgnoreCase))
                    issues.Add($"索引 {expected.Name} 的过滤条件不符合 v9 合同");
            }
        }

        foreach (var (table, columns) in new[]
                 {
                     ("roles", new[] { "name" }), ("role_permissions", new[] { "role_id", "permission_code" }),
                     ("users", new[] { "login_name" }), ("model_point_configs", new[] { "product_model_id", "test_item_point_id" }),
                     ("test_records", new[] { "record_number" }), ("maintenance_schedules", new[] { "asset_id", "kind" })
                 })
        {
            if (!inspection.Indexes.Any(index => index.Table.Equals(table, StringComparison.OrdinalIgnoreCase)
                                                 && index.IsUnique
                                                 && index.Columns.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase)))
                issues.Add($"{table} 缺少唯一约束 ({string.Join(",", columns)})");
        }

        if (!inspection.Columns.TryGetValue("test_records", out var recordColumns)
            || !recordColumns.Any(column => column.Name.Equals("run_id", StringComparison.OrdinalIgnoreCase) && column.NotNull))
            issues.Add("test_records.run_id 必须为 NOT NULL");
        if (!inspection.Columns.TryGetValue("test_records", out recordColumns)
            || !recordColumns.Any(column => column.Name.Equals("bench_id", StringComparison.OrdinalIgnoreCase)))
            issues.Add("test_records 缺少 bench_id");
        return issues;
    }

    private static string? IndexPredicate(string? sql)
    {
        if (sql is null) return null;
        var match = System.Text.RegularExpressions.Regex.Match(sql, @"\bWHERE\b(.*)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        return match.Success
            ? System.Text.RegularExpressions.Regex.Replace(match.Groups[1].Value.Trim().TrimEnd(';'), @"\s+", " ")
            : null;
    }
}
