namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 当前生产数据库 v8 的最终结构合同。历史版本转换只在离线升级工具中处理。
/// </summary>
public static class SqliteSchemaV8
{
    public const int Version = 8;

    public static IReadOnlyList<string> Tables { get; } = new[]
    {
        "app_metadata", "roles", "role_permissions", "users", "product_types",
        "product_models", "test_item_points", "model_point_configs",
        "project_test_parameters", "product_test_parameters", "test_records",
        "test_item_results", "report_records", "audit_logs"
    };

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Columns { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_metadata"] = new[] { "key", "value" },
            ["roles"] = new[] { "id", "name", "system_key" },
            ["role_permissions"] = new[] { "role_id", "permission_code" },
            ["users"] = new[] { "id", "login_name", "display_name", "password_hash", "must_change_password", "is_enabled", "failed_login_count", "locked_until_utc", "role_id", "created_at_utc" },
            ["product_types"] = new[] { "id", "name", "is_enabled", "created_at_utc" },
            ["product_models"] = new[] { "id", "product_type_id", "name", "is_enabled", "created_at_utc" },
            ["test_item_points"] = new[] { "id", "product_type_id", "name", "executor_code", "result_kind", "is_enabled", "sort_order", "created_at_utc", "updated_at_utc" },
            ["model_point_configs"] = new[] { "id", "product_model_id", "test_item_point_id", "sort_order", "configured_at_utc" },
            ["project_test_parameters"] = new[] { "scope_key", "test_time_seconds", "updated_by", "updated_at_utc" },
            ["product_test_parameters"] = new[] { "product_model_id", "product_type_id", "test_voltage_v", "protect_current_ma", "updated_by", "updated_at_utc" },
            ["test_records"] = new[] { "id", "record_number", "product_model_id", "product_number", "batch_number", "station_number", "remark", "parameter_snapshot", "sequence_snapshot", "device_mode", "operator_user_id", "state", "conclusion", "started_at_utc", "finished_at_utc", "device_configuration_revision", "signal_bindings_snapshot" },
            ["test_item_results"] = new[] { "id", "record_id", "test_item_point_id", "state", "summary_value", "result_text", "started_at_utc", "finished_at_utc" },
            ["report_records"] = new[] { "id", "test_record_id", "template_path", "output_path", "status", "error", "created_by_user_id", "created_at_utc", "completed_at_utc" },
            ["audit_logs"] = new[] { "id", "actor", "action", "target", "detail", "created_at_utc" }
        };

    public static IReadOnlyList<string> CreateStatements { get; } = new[]
    {
        "CREATE TABLE app_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL)",
        "CREATE TABLE roles (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, system_key TEXT NULL)",
        "CREATE UNIQUE INDEX ux_roles_system_key ON roles(system_key) WHERE system_key IS NOT NULL",
        "CREATE TABLE role_permissions (role_id INTEGER NOT NULL, permission_code INTEGER NOT NULL, PRIMARY KEY (role_id, permission_code), FOREIGN KEY (role_id) REFERENCES roles(id))",
        "CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, login_name TEXT NOT NULL UNIQUE, display_name TEXT NOT NULL, password_hash TEXT NOT NULL, must_change_password INTEGER NOT NULL DEFAULT 1, is_enabled INTEGER NOT NULL DEFAULT 1, failed_login_count INTEGER NOT NULL DEFAULT 0, locked_until_utc TEXT NULL, role_id INTEGER NOT NULL, created_at_utc TEXT NOT NULL, FOREIGN KEY (role_id) REFERENCES roles(id))",
        "CREATE TABLE product_types (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, is_enabled INTEGER NOT NULL DEFAULT 1, created_at_utc TEXT NOT NULL)",
        "CREATE TABLE product_models (id INTEGER PRIMARY KEY AUTOINCREMENT, product_type_id INTEGER NOT NULL, name TEXT NOT NULL, is_enabled INTEGER NOT NULL DEFAULT 1, created_at_utc TEXT NOT NULL, FOREIGN KEY (product_type_id) REFERENCES product_types(id))",
        "CREATE TABLE test_item_points (id INTEGER PRIMARY KEY AUTOINCREMENT, product_type_id INTEGER NOT NULL, name TEXT NOT NULL, executor_code TEXT NOT NULL, result_kind TEXT NOT NULL DEFAULT 'PassFail', is_enabled INTEGER NOT NULL DEFAULT 1, sort_order INTEGER NOT NULL DEFAULT 0, created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL, FOREIGN KEY (product_type_id) REFERENCES product_types(id))",
        "CREATE TABLE model_point_configs (id INTEGER PRIMARY KEY AUTOINCREMENT, product_model_id INTEGER NOT NULL, test_item_point_id INTEGER NOT NULL, sort_order INTEGER NOT NULL, configured_at_utc TEXT NOT NULL, UNIQUE (product_model_id, test_item_point_id), FOREIGN KEY (product_model_id) REFERENCES product_models(id), FOREIGN KEY (test_item_point_id) REFERENCES test_item_points(id))",
        "CREATE TABLE project_test_parameters (scope_key TEXT PRIMARY KEY, test_time_seconds INTEGER NOT NULL, updated_by TEXT NOT NULL, updated_at_utc TEXT NOT NULL)",
        "CREATE TABLE product_test_parameters (product_model_id INTEGER PRIMARY KEY, product_type_id INTEGER NOT NULL, test_voltage_v REAL NOT NULL, protect_current_ma REAL NOT NULL, updated_by TEXT NOT NULL, updated_at_utc TEXT NOT NULL, FOREIGN KEY (product_type_id) REFERENCES product_types(id), FOREIGN KEY (product_model_id) REFERENCES product_models(id))",
        "CREATE TABLE test_records (id INTEGER PRIMARY KEY AUTOINCREMENT, record_number TEXT NOT NULL UNIQUE, product_model_id INTEGER NOT NULL, product_number TEXT NULL, batch_number TEXT NULL, station_number TEXT NULL, remark TEXT NULL, parameter_snapshot TEXT NULL, sequence_snapshot TEXT NULL, device_mode INTEGER NOT NULL, operator_user_id INTEGER NOT NULL, state INTEGER NOT NULL DEFAULT 0, conclusion TEXT NULL, started_at_utc TEXT NOT NULL, finished_at_utc TEXT NULL, device_configuration_revision TEXT NULL, signal_bindings_snapshot TEXT NULL, FOREIGN KEY (product_model_id) REFERENCES product_models(id), FOREIGN KEY (operator_user_id) REFERENCES users(id))",
        "CREATE TABLE test_item_results (id INTEGER PRIMARY KEY AUTOINCREMENT, record_id INTEGER NOT NULL, test_item_point_id INTEGER NOT NULL, state INTEGER NOT NULL DEFAULT 0, summary_value TEXT NULL, result_text TEXT NULL, started_at_utc TEXT NULL, finished_at_utc TEXT NULL, FOREIGN KEY (record_id) REFERENCES test_records(id))",
        "CREATE TABLE report_records (id INTEGER PRIMARY KEY AUTOINCREMENT, test_record_id INTEGER NOT NULL, template_path TEXT NOT NULL, output_path TEXT NULL, status INTEGER NOT NULL DEFAULT 0, error TEXT NULL, created_by_user_id INTEGER NOT NULL, created_at_utc TEXT NOT NULL, completed_at_utc TEXT NULL, FOREIGN KEY (test_record_id) REFERENCES test_records(id), FOREIGN KEY (created_by_user_id) REFERENCES users(id))",
        "CREATE TABLE audit_logs (id INTEGER PRIMARY KEY AUTOINCREMENT, actor TEXT NOT NULL, action TEXT NOT NULL, target TEXT NULL, detail TEXT NULL, created_at_utc TEXT NOT NULL)"
    };

    public static IReadOnlyList<string> Validate(SqliteDatabaseInspection inspection)
    {
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
        issues.AddRange(actualTables.Except(expectedTables, StringComparer.OrdinalIgnoreCase).Select(name => $"存在 v8 合同外的表 {name}"));
        var unexpectedObjects = inspection.Objects.Where(item =>
            !item.Type.Equals("table", StringComparison.OrdinalIgnoreCase)
            && !(item.Type.Equals("index", StringComparison.OrdinalIgnoreCase)
                 && item.Name.Equals("ux_roles_system_key", StringComparison.OrdinalIgnoreCase)));
        issues.AddRange(unexpectedObjects.Select(item => $"存在未登记的 SQLite 对象 {item.Type}:{item.Name}"));

        foreach (var (table, expectedColumns) in Columns)
        {
            if (!inspection.Columns.TryGetValue(table, out var actualColumns))
                continue;
            var actual = actualColumns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expected = expectedColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            issues.AddRange(expected.Except(actual, StringComparer.OrdinalIgnoreCase).Select(name => $"{table} 缺少列 {name}"));
            issues.AddRange(actual.Except(expected, StringComparer.OrdinalIgnoreCase).Select(name => $"{table} 存在合同外列 {name}"));
        }

        if (!string.Equals(inspection.AppMetadataSchemaVersion, Version.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            issues.Add($"app_metadata.schema_version 应为 {Version}，实际为 {inspection.AppMetadataSchemaVersion ?? "缺失"}");
        if (!inspection.Indexes.Any(index => index.Table.Equals("roles", StringComparison.OrdinalIgnoreCase)
                && index.Name.Equals("ux_roles_system_key", StringComparison.OrdinalIgnoreCase)
                && index.IsUnique && index.IsPartial
                && index.Columns.SequenceEqual(new[] { "system_key" }, StringComparer.OrdinalIgnoreCase)))
            issues.Add("roles 缺少唯一的 ux_roles_system_key 部分索引");

        var expectedPrimaryKeys = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_metadata"] = new[] { "key" },
            ["roles"] = new[] { "id" },
            ["role_permissions"] = new[] { "role_id", "permission_code" },
            ["users"] = new[] { "id" },
            ["product_types"] = new[] { "id" },
            ["product_models"] = new[] { "id" },
            ["test_item_points"] = new[] { "id" },
            ["model_point_configs"] = new[] { "id" },
            ["project_test_parameters"] = new[] { "scope_key" },
            ["product_test_parameters"] = new[] { "product_model_id" },
            ["test_records"] = new[] { "id" },
            ["test_item_results"] = new[] { "id" },
            ["report_records"] = new[] { "id" },
            ["audit_logs"] = new[] { "id" }
        };
        foreach (var (table, expectedPrimaryKey) in expectedPrimaryKeys)
        {
            if (!inspection.Columns.TryGetValue(table, out var columns))
                continue;
            var actualPrimaryKey = columns.Where(column => column.IsPrimaryKey).Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actualPrimaryKey.SetEquals(expectedPrimaryKey))
                issues.Add($"{table} 主键结构不符");
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
        issues.AddRange(expectedForeignKeys.Except(actualForeignKeys, StringComparer.OrdinalIgnoreCase).Select(item => $"缺少外键 {item}"));
        issues.AddRange(actualForeignKeys.Except(expectedForeignKeys, StringComparer.OrdinalIgnoreCase).Select(item => $"存在合同外外键 {item}"));

        bool HasUniqueIndex(string table, params string[] expectedColumns)
            => inspection.Indexes.Any(index => index.Table.Equals(table, StringComparison.OrdinalIgnoreCase)
                && index.IsUnique
                && index.Columns.SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase));
        foreach (var (table, columns) in new[]
                 {
                     ("roles", new[] { "name" }),
                     ("role_permissions", new[] { "role_id", "permission_code" }),
                     ("users", new[] { "login_name" }),
                     ("model_point_configs", new[] { "product_model_id", "test_item_point_id" }),
                     ("test_records", new[] { "record_number" })
                 })
            if (!HasUniqueIndex(table, columns))
                issues.Add($"{table} 缺少唯一约束 ({string.Join(",", columns)})");

        return issues;
    }

}
