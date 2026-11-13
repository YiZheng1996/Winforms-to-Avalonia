using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Upgrade;

namespace XXX.TestBench.Integration.Tests;

public sealed class OfflineUpgradeTests
{
    [Fact]
    public async Task CheckAndConvertV7_PreservesSourceAndRetainedBusinessData()
    {
        var root = NewTempDirectory();
        var source = Path.Combine(root, "source-v7.db");
        var output = Path.Combine(root, "converted-v8.db");
        CreateV7Database(source);
        var beforeHash = HashFile(source);

        var upgrader = new SqliteV7ToV8Upgrade();
        var check = await upgrader.CheckAsync(source);
        Assert.True(check.Success, string.Join("; ", check.Blockers));
        Assert.Equal(0, check.FailureCode);
        Assert.Equal("1", await ScalarAsync<string>(source, "SELECT value FROM app_metadata WHERE key='schema_version'"));
        Assert.Equal(beforeHash, HashFile(source));

        var converted = await upgrader.ConvertAsync(source, output);
        Assert.True(converted.Success, string.Join("; ", converted.Blockers));
        Assert.True(converted.OutputWritten);
        Assert.Equal(beforeHash, HashFile(source));
        Assert.Equal(7, (await SqliteDatabaseInspector.InspectReadOnlyAsync(source)).UserVersion);

        var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(output);
        Assert.Empty(SqliteSchemaV8.Validate(inspection));
        Assert.Equal(8, inspection.UserVersion);
        Assert.DoesNotContain(inspection.Columns["product_types"], column => column.Name == "code");
        Assert.DoesNotContain(inspection.Columns["product_models"], column => column.Name == "code");
        Assert.DoesNotContain(inspection.Columns["test_item_points"], column => column.Name == "code");
        Assert.Equal("8", await ScalarAsync<string>(output, "SELECT value FROM app_metadata WHERE key='schema_version'"));
        Assert.Equal("raw-sequence-snapshot", await ScalarAsync<string>(output, "SELECT sequence_snapshot FROM test_records WHERE id=80"));
        Assert.Equal("raw-signal-snapshot", await ScalarAsync<string>(output, "SELECT signal_bindings_snapshot FROM test_records WHERE id=80"));
        Assert.Equal("raw-report-path", await ScalarAsync<string>(output, "SELECT output_path FROM report_records WHERE id=100"));
        Assert.Equal(1000L, await ScalarAsync<long>(output, "SELECT seq FROM sqlite_sequence WHERE name='product_types'"));
        Assert.Equal(2000L, await ScalarAsync<long>(output, "SELECT seq FROM sqlite_sequence WHERE name='product_models'"));
        Assert.Equal(3000L, await ScalarAsync<long>(output, "SELECT seq FROM sqlite_sequence WHERE name='test_item_points'"));
        Assert.Equal(0L, await ScalarAsync<long>(output, "SELECT COUNT(*) FROM role_permissions WHERE permission_code=7"));

        var adminId = await ScalarAsync<long>(output, "SELECT id FROM roles WHERE system_key='Administrator'");
        var adminPermissions = await ReadPermissionsAsync(output, adminId);
        Assert.Equal(PermissionCatalog.ActiveCodes.Select(code => (int)code).Order().ToArray(), adminPermissions.Order().ToArray());
        var operatorPermissions = await ReadPermissionsAsync(output, 2);
        Assert.Equal(new[] { 1 }, operatorPermissions.Order().ToArray());
    }

    [Fact]
    public async Task CheckDb_RejectsUnsupportedVersionAndUnknownPermissionWithoutWritingSource()
    {
        var root = NewTempDirectory();
        var unsupported = Path.Combine(root, "unsupported-v6.db");
        CreateV7Database(unsupported);
        await ExecuteAsync(unsupported, "PRAGMA user_version=6");
        var unsupportedHash = HashFile(unsupported);
        var unsupportedReport = await new SqliteV7ToV8Upgrade().CheckAsync(unsupported);
        Assert.False(unsupportedReport.Success);
        Assert.Equal(2, unsupportedReport.FailureCode);
        Assert.Equal(unsupportedHash, HashFile(unsupported));

        var unknownPermission = Path.Combine(root, "unknown-permission-v7.db");
        CreateV7Database(unknownPermission);
        await ExecuteAsync(unknownPermission,
            "INSERT INTO role_permissions(role_id,permission_code) VALUES (2,999)");
        var permissionHash = HashFile(unknownPermission);
        var permissionReport = await new SqliteV7ToV8Upgrade().CheckAsync(unknownPermission);
        Assert.False(permissionReport.Success);
        Assert.Equal(3, permissionReport.FailureCode);
        Assert.Contains(permissionReport.Blockers, item => item.Contains("permission_code=999", StringComparison.Ordinal));
        Assert.Equal(permissionHash, HashFile(unknownPermission));
    }

    [Fact]
    public async Task CheckDb_RejectsRunningRecordsAndExtensions()
    {
        var root = NewTempDirectory();
        var running = Path.Combine(root, "running-v7.db");
        CreateV7Database(running);
        await ExecuteAsync(running, "UPDATE test_records SET state=0 WHERE id=80");
        var runningReport = await new SqliteV7ToV8Upgrade().CheckAsync(running);
        Assert.False(runningReport.Success);
        Assert.Equal(3, runningReport.FailureCode);
        Assert.Contains(runningReport.Blockers, item => item.Contains("Running", StringComparison.Ordinal));

        var extension = Path.Combine(root, "extension-v7.db");
        CreateV7Database(extension);
        await ExecuteAsync(extension,
            "CREATE TRIGGER custom_product_types_trigger AFTER UPDATE ON product_types BEGIN SELECT 1; END");
        var extensionReport = await new SqliteV7ToV8Upgrade().CheckAsync(extension);
        Assert.False(extensionReport.Success);
        Assert.Equal(3, extensionReport.FailureCode);
        Assert.Contains(extensionReport.Blockers, item => item.Contains("扩展对象", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigUpgrade_ConvertsSupportedFilesIntoValidatedRevisionAndKeepsSourceUnchanged()
    {
        var root = NewTempDirectory();
        var source = Path.Combine(root, "legacy-files");
        var output = Path.Combine(root, "converted-config");
        WriteLegacyV3V2V2(source, missingBindings: true);
        var hashes = HashInputs(source);
        var service = new ConfigurationUpgradeService();

        var check = await service.CheckAsync(source, "files");
        Assert.True(check.Success, string.Join("; ", check.Blockers));
        Assert.Equal(0, check.FailureCode);
        Assert.Equal(hashes, HashInputs(source));
        Assert.Contains(check.Warnings, warning => warning.Contains("signal-bindings.json", StringComparison.Ordinal));
        Assert.Equal("device=3;points=2;simulation=2;bindings=synthesized-empty-v1", check.SourceConfigurationVersions);
        Assert.Equal("device=5;points=4;simulation=3;bindings=1", check.OutputConfigurationVersions);

        var converted = await service.ConvertAsync(source, "files", output);
        Assert.True(converted.Success, string.Join("; ", converted.Blockers));
        Assert.True(converted.OutputWritten);
        Assert.Equal(hashes, HashInputs(source));
        Assert.StartsWith("sha256-", new DirectoryInfo(Directory.GetDirectories(Path.Combine(output, "device-config", "revisions")).Single()).Name);

        await using var store = new DeviceConfigurationStore(output);
        var snapshot = await store.LoadActiveAsync();
        Assert.Equal(DeviceConfig.CurrentSchemaVersion, snapshot.Device.SchemaVersion);
        Assert.Equal(PointsConfig.CurrentSchemaVersion, snapshot.Points.SchemaVersion);
        Assert.Equal(SimulationConfig.CurrentSchemaVersion, snapshot.Simulation.SchemaVersion);
        Assert.Equal("Bool", snapshot.Points.Points.Single().DataType);
        Assert.Equal("127.0.0.1", snapshot.Device.Devices.Single().SiemensS7!.Host);
        Assert.Equal(1100, snapshot.Device.Devices.Single().StaleAfterMs);
        Assert.Single(snapshot.Points.Groups);
        Assert.Empty(snapshot.SignalBindings.Bindings);

        var revisionCheck = await service.CheckAsync(output, "revision");
        Assert.True(revisionCheck.Success, string.Join("; ", revisionCheck.Blockers));
        Assert.Contains(revisionCheck.Changes, change => change.StartsWith("NoChange", StringComparison.Ordinal));
        var noChangeOutput = Path.Combine(root, "no-change-output");
        var noChange = await service.ConvertAsync(output, "revision", noChangeOutput);
        Assert.True(noChange.Success, string.Join("; ", noChange.Blockers));
        Assert.False(noChange.OutputWritten);
        Assert.False(Directory.Exists(noChangeOutput));
    }

    [Fact]
    public async Task ConfigUpgrade_RejectsConflictingTypeAndLegacyS7Hosts()
    {
        var root = NewTempDirectory();
        var typeConflict = Path.Combine(root, "type-conflict");
        WriteLegacyV3V2V2(typeConflict, missingBindings: false, rawDataType: "Float32");
        var typeReport = await new ConfigurationUpgradeService().CheckAsync(typeConflict, "files");
        Assert.False(typeReport.Success);
        Assert.Equal(3, typeReport.FailureCode);
        Assert.Contains(typeReport.Blockers, blocker => blocker.Contains("dataType 与 rawDataType 冲突", StringComparison.Ordinal));

        var endpointConflict = Path.Combine(root, "endpoint-conflict");
        WriteLegacyV3V2V2(endpointConflict, missingBindings: false, address: "10.0.0.2");
        var endpointReport = await new ConfigurationUpgradeService().CheckAsync(endpointConflict, "files");
        Assert.False(endpointReport.Success);
        Assert.Equal(3, endpointReport.FailureCode);
        Assert.Contains(endpointReport.Blockers, blocker => blocker.Contains("Host/Address 来源互相冲突", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigUpgrade_PreservesExplicitV4TimingRackSlotAndGroupIdentity()
    {
        var root = NewTempDirectory();
        var source = Path.Combine(root, "legacy-v4");
        WriteLegacyV4V3V2(source);
        var output = Path.Combine(root, "converted-v4");
        var result = await new ConfigurationUpgradeService().ConvertAsync(source, "files", output);
        Assert.True(result.Success, string.Join("; ", result.Blockers));

        await using var store = new DeviceConfigurationStore(output);
        var snapshot = await store.LoadActiveAsync();
        var device = Assert.Single(snapshot.Device.Devices);
        Assert.Equal(2500, device.Timing.ConnectTimeoutMs);
        Assert.Equal(600, device.PollIntervalMs);
        Assert.Equal(DeviceScanMode.OnDemand, device.ScanMode);
        Assert.Equal(1, device.SiemensS7!.Rack);
        Assert.Equal(2, device.SiemensS7.Slot);
        Assert.Equal(50000, device.AutoDemotion.DemotionPeriodMs);
        Assert.Equal("50000000-0000-5000-8000-000000000001", snapshot.Points.Groups.Single().Id);
        Assert.Equal(0m, snapshot.Points.Points.Single().RawMin);
        Assert.Equal(100m, snapshot.Points.Points.Single().RawMax);
    }

    private static void CreateV7Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        foreach (var sql in SqliteSchemaV8.CreateStatements)
        {
            var v7Sql = sql switch
            {
                var value when value.StartsWith("CREATE TABLE product_types ", StringComparison.Ordinal)
                    => "CREATE TABLE product_types (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT NOT NULL UNIQUE, name TEXT NOT NULL, is_enabled INTEGER NOT NULL DEFAULT 1, created_at_utc TEXT NOT NULL)",
                var value when value.StartsWith("CREATE TABLE product_models ", StringComparison.Ordinal)
                    => "CREATE TABLE product_models (id INTEGER PRIMARY KEY AUTOINCREMENT, product_type_id INTEGER NOT NULL, code TEXT NOT NULL, name TEXT NOT NULL, is_enabled INTEGER NOT NULL DEFAULT 1, created_at_utc TEXT NOT NULL, UNIQUE(product_type_id, code), FOREIGN KEY (product_type_id) REFERENCES product_types(id))",
                var value when value.StartsWith("CREATE TABLE test_item_points ", StringComparison.Ordinal)
                    => "CREATE TABLE test_item_points (id INTEGER PRIMARY KEY AUTOINCREMENT, product_type_id INTEGER NOT NULL, code TEXT NOT NULL, name TEXT NOT NULL, executor_code TEXT NOT NULL, result_kind TEXT NOT NULL DEFAULT 'PassFail', is_enabled INTEGER NOT NULL DEFAULT 1, sort_order INTEGER NOT NULL DEFAULT 0, created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL, UNIQUE(product_type_id, code), FOREIGN KEY (product_type_id) REFERENCES product_types(id))",
                _ => sql
            };
            Execute(connection, null, v7Sql);
        }
        Execute(connection, null, "INSERT INTO app_metadata(key,value) VALUES('schema_version','1')");
        Execute(connection, null, "PRAGMA user_version=7");
        Execute(connection, null, "INSERT INTO roles(id,name,system_key) VALUES(1,'Administrator','Administrator'),(2,'Operator','Operator')");
        Execute(connection, null, "INSERT INTO role_permissions(role_id,permission_code) VALUES(1,7),(1,1),(2,1)");
        Execute(connection, null, "INSERT INTO users(id,login_name,display_name,password_hash,must_change_password,is_enabled,failed_login_count,role_id,created_at_utc) VALUES(1,'admin','Admin','redacted-hash',1,1,0,1,'2026-09-29T00:00:00Z'),(2,'operator','Operator','other-hash',0,1,0,2,'2026-09-29T00:00:00Z')");
        Execute(connection, null, "INSERT INTO product_types(id,code,name,is_enabled,created_at_utc) VALUES(42,'TYPE-1','Type 1',1,'created-type')");
        Execute(connection, null, "INSERT INTO product_models(id,product_type_id,code,name,is_enabled,created_at_utc) VALUES(50,42,'MODEL-1','Model 1',1,'created-model')");
        Execute(connection, null, "INSERT INTO test_item_points(id,product_type_id,code,name,executor_code,result_kind,is_enabled,sort_order,created_at_utc,updated_at_utc) VALUES(60,42,'ITEM-1','Item 1','VoltageTest','PassFail',1,12,'created-item','updated-item')");
        Execute(connection, null, "INSERT INTO model_point_configs(id,product_model_id,test_item_point_id,sort_order,configured_at_utc) VALUES(70,50,60,3,'configured')");
        Execute(connection, null, "INSERT INTO project_test_parameters(scope_key,test_time_seconds,updated_by,updated_at_utc) VALUES('project',30,'admin','project-updated')");
        Execute(connection, null, "INSERT INTO product_test_parameters(product_model_id,product_type_id,test_voltage_v,protect_current_ma,updated_by,updated_at_utc) VALUES(50,42,220.5,20.25,'admin','product-updated')");
        Execute(connection, null, "INSERT INTO test_records(id,record_number,product_model_id,product_number,batch_number,station_number,remark,parameter_snapshot,sequence_snapshot,device_mode,operator_user_id,state,conclusion,started_at_utc,finished_at_utc,device_configuration_revision,signal_bindings_snapshot) VALUES(80,'REC-80',50,'P-80','B-80','S-80','remark','parameter-snapshot','raw-sequence-snapshot',1,1,1,'Pass','started','finished','revision-7','raw-signal-snapshot')");
        Execute(connection, null, "INSERT INTO test_item_results(id,record_id,test_item_point_id,state,summary_value,result_text,started_at_utc,finished_at_utc) VALUES(90,80,60,1,'1.25','PASS','result-start','result-end')");
        Execute(connection, null, "INSERT INTO report_records(id,test_record_id,template_path,output_path,status,error,created_by_user_id,created_at_utc,completed_at_utc) VALUES(100,80,'template','raw-report-path',1,NULL,1,'report-created','report-completed')");
        Execute(connection, null, "INSERT INTO audit_logs(id,actor,action,target,detail,created_at_utc) VALUES(110,'admin','Saved','REC-80','audit-detail','audit-created')");
        Execute(connection, null, "UPDATE sqlite_sequence SET seq=1000 WHERE name='product_types'");
        Execute(connection, null, "UPDATE sqlite_sequence SET seq=2000 WHERE name='product_models'");
        Execute(connection, null, "UPDATE sqlite_sequence SET seq=3000 WHERE name='test_item_points'");
    }

    private static void WriteLegacyV3V2V2(string root, bool missingBindings, string rawDataType = "Boolean", string address = "127.0.0.1")
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "device.json"), $$$"""
        {
          "schemaVersion": 3,
          "channels": [{"id":"10000000-0000-5000-8000-000000000001","code":"CH1","name":"TCP","transportKind":"Tcp","enabled":true,"timeoutMs":1000,"retryCount":0,"tcp":{"host":"127.0.0.1","port":102}}],
          "devices": [{"id":"20000000-0000-5000-8000-000000000001","code":"PLC1","name":"PLC 1","deviceMode":"Simulation","protocol":"SiemensS7","driverKey":"siemens-s7","address":"{{{address}}}","channelId":"10000000-0000-5000-8000-000000000001","model":"S7-1500","pollIntervalMs":500,"staleAfterMs":2000,"enabled":true}]
        }
        """);
        File.WriteAllText(Path.Combine(root, "points.json"), $$$"""
        {
          "schemaVersion": 2,
          "points": [{"id":"40000000-0000-5000-8000-000000000001","code":"P1","name":"Pressure","protocol":"SiemensS7","deviceId":"20000000-0000-5000-8000-000000000001","address":"DB1.DBX0.0","dataType":"Boolean","rawDataType":"{{{rawDataType}}}","decodeOptions":{"byteOrder":"BigEndian","wordOrder":"None"},"writePolicy":"ReadBackEqual","isWritable":false,"riskLevel":"Normal"}]
        }
        """);
        File.WriteAllText(Path.Combine(root, "simulation.json"), """
        {
          "schemaVersion": 2,
          "initialValues": {},
          "initialValuesByPointId": {"40000000-0000-5000-8000-000000000001":false},
          "changeRules": [],
          "faultInjectionScenarios": []
        }
        """);
        if (!missingBindings)
            File.WriteAllText(Path.Combine(root, "signal-bindings.json"), """
            {"schemaVersion":1,"bindings":{}}
            """);
    }

    private static void WriteLegacyV4V3V2(string root)
    {
        WriteLegacyV3V2V2(root, missingBindings: false);
        var devicePath = Path.Combine(root, "device.json");
        var device = JsonNode.Parse(File.ReadAllText(devicePath))!.AsObject();
        device["schemaVersion"] = 4;
        var entry = device["devices"]!.AsArray()[0]!.AsObject();
        entry["pollIntervalMs"] = 600;
        entry["staleAfterMs"] = 1200;
        entry["siemensS7"] = new System.Text.Json.Nodes.JsonObject
        {
            ["host"] = "127.0.0.1", ["port"] = 102, ["rack"] = 1, ["slot"] = 2
        };
        entry["scanMode"] = "OnDemand";
        entry["timing"] = new System.Text.Json.Nodes.JsonObject
        {
            ["connectTimeoutMs"] = 2500, ["requestTimeoutMs"] = 800,
            ["retryCount"] = 1, ["interRequestDelayMs"] = 5
        };
        entry["autoDemotion"] = new System.Text.Json.Nodes.JsonObject
        {
            ["enabled"] = false, ["failureThreshold"] = 4, ["demotionPeriodMs"] = 50000,
            ["discardWritesWhileDemoted"] = false
        };
        File.WriteAllText(devicePath, device.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var pointsPath = Path.Combine(root, "points.json");
        var points = JsonNode.Parse(File.ReadAllText(pointsPath))!.AsObject();
        points["schemaVersion"] = 3;
        points["groups"] = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = "50000000-0000-5000-8000-000000000001",
                ["deviceId"] = "20000000-0000-5000-8000-000000000001",
                ["code"] = "PRESSURE", ["name"] = "Pressure", ["description"] = "group", ["sortOrder"] = 3
            }
        };
        var point = points["points"]!.AsArray()[0]!.AsObject();
        point["groupId"] = "50000000-0000-5000-8000-000000000001";
        point["dataType"] = "Float32";
        point["rawDataType"] = "Float32";
        point["address"] = "DB1.DBD0";
        point["rawMin"] = 0;
        point["rawMax"] = 100;
        point["engMin"] = 0;
        point["engMax"] = 10;
        point["scale"] = new System.Text.Json.Nodes.JsonObject
        {
            ["rawMin"] = 0, ["rawMax"] = 100, ["engMin"] = 0, ["engMax"] = 10
        };
        File.WriteAllText(pointsPath, points.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var simulationPath = Path.Combine(root, "simulation.json");
        var simulation = JsonNode.Parse(File.ReadAllText(simulationPath))!.AsObject();
        simulation["initialValuesByPointId"]!["40000000-0000-5000-8000-000000000001"] = 1.25;
        simulation["changeRules"] = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["pointId"] = "40000000-0000-5000-8000-000000000001", ["address"] = "DB1.DBD0",
                ["pattern"] = "ramp", ["ratePerSecond"] = 0.1, ["min"] = 0, ["max"] = 10
            }
        };
        File.WriteAllText(simulationPath, simulation.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "testbench-offline-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, string> HashInputs(string root)
        => Directory.GetFiles(root, "*.json").ToDictionary(path => Path.GetFileName(path)!, HashFile, StringComparer.Ordinal);

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static async Task ExecuteAsync(string path, string sql)
    {
        using var connection = Open(path);
        await connection.OpenAsync();
        Execute(connection, null, sql);
    }

    private static async Task<T> ScalarAsync<T>(string path, string sql)
    {
        using var connection = Open(path);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int[]> ReadPermissionsAsync(string path, long roleId)
    {
        using var connection = Open(path);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT permission_code FROM role_permissions WHERE role_id=@roleId ORDER BY permission_code";
        command.Parameters.AddWithValue("@roleId", roleId);
        using var reader = await command.ExecuteReaderAsync();
        var values = new List<int>();
        while (await reader.ReadAsync()) values.Add(reader.GetInt32(0));
        return values.ToArray();
    }

    private static SQLiteConnection Open(string path)
        => new(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString());

    private static void Execute(SQLiteConnection connection, SQLiteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
