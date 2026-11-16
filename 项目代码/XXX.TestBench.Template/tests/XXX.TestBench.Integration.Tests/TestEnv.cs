using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Devices.Executors;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Integration.Tests;

/// <summary>
/// 集成测试环境：在临时目录写 config 样例并给出数据目录。
/// </summary>
public sealed class TestEnv : IDisposable
{
    public string Root { get; }
    public string ConfigRoot { get; }
    public string DataRoot { get; }
    public string DbPath { get; }

    private TestEnv(string root, string deviceMode)
    {
        Root = root;
        ConfigRoot = Path.Combine(root, "config");
        DataRoot = Path.Combine(root, "data");
        DbPath = Path.Combine(DataRoot, "testbench.db");
        Directory.CreateDirectory(ConfigRoot);
        Directory.CreateDirectory(DataRoot);
        WriteConfigs(deviceMode);
    }

    public static TestEnv Create(string deviceMode = "Simulation")
        => new(Path.Combine(Path.GetTempPath(), "testbench-int-" + Guid.NewGuid().ToString("N")[..8]), deviceMode);

    public static IReadOnlyList<RequiredSignal> RegisteredRequiredSignals()
    {
        var executors = new ExecutorFactory();
        return executors.Codes
            .SelectMany(code => executors.Get(code).RequiredSignals)
            .ToArray();
    }

    public async Task<DeviceConfigurationSnapshot> LoadSnapshotAsync(CancellationToken ct = default)
    {
        var store = new JsonConfigStore(ConfigRoot);
        return new DeviceConfigurationSnapshot
        {
            Revision = "test-" + Guid.NewGuid().ToString("N"),
            Device = await store.LoadAsync<DeviceConfig>("device.json", ct),
            Points = await store.LoadAsync<PointsConfig>("points.json", ct),
            Simulation = await store.LoadAsync<SimulationConfig>("simulation.json", ct),
            SignalBindings = await store.LoadAsync<SignalBindingsConfig>("signal-bindings.json", ct)
        };
    }

    private void WriteConfigs(string deviceMode)
    {
        File.WriteAllText(Path.Combine(ConfigRoot, "app.json"), $$"""
        {
          "schemaVersion": 1,
          "systemName": "测试系统",
          "brand": "XXX.TestBench.Template",
          "language": "zh-CN",
          "modules": { "processMonitor": true, "deviceCalibration": true },
          "defaultPaths": { "database": "testbench.db", "reportOutput": "reports", "reportTemplates": "assets/report-templates" }
        }
        """);
        File.WriteAllText(Path.Combine(ConfigRoot, "device.json"), $$"""
        {
          "schemaVersion": 5,
          "channels": [
            { "id": "10000000-0000-0000-0000-000000000001", "code": "CH_PLC", "name": "PLC 网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": {} }
          ],
          "devices": [
            { "id": "20000000-0000-0000-0000-000000000001", "code": "DEV_SAMPLE", "name": "SampleDevice", "deviceMode": "{{deviceMode}}", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "siemensS7": { "host": "127.0.0.1", "port": 102, "rack": 0, "slot": 0 }, "pollIntervalMs": 500, "staleAfterMs": 1500 }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(ConfigRoot, "points.json"), """
        {
          "schemaVersion": 4,
          "groups": [
            { "id": "30000000-0000-0000-0000-000000000001", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 }
          ],
          "points": [
            { "id": "40000000-0000-0000-0000-000000000001", "code": "AI_Pressure", "name": "压力", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000001", "address": "DB1.DBD0", "dataType": "Float32", "isWritable": false, "riskLevel": "Normal" },
            { "id": "40000000-0000-0000-0000-000000000002", "code": "DO_Start", "name": "启动", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000001", "address": "DB1.DBX4.0", "dataType": "Bool", "writePolicy": "ReadBackEqual", "isWritable": true, "riskLevel": "HighRisk" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(ConfigRoot, "simulation.json"), """
        {
          "schemaVersion": 3,
          "initialValuesByPointId": {
            "40000000-0000-0000-0000-000000000001": 0.0,
            "40000000-0000-0000-0000-000000000002": false
          },
          "changeRules": [ { "pointId": "40000000-0000-0000-0000-000000000001", "pattern": "ramp", "ratePerSecond": 0.1, "max": 10.0, "min": 0.0 } ],
          "faultInjectionScenarios": []
        }
        """);
        File.WriteAllText(Path.Combine(ConfigRoot, "signal-bindings.json"), """
        {
          "schemaVersion": 1,
          "bindings": { "AI_Pressure": "40000000-0000-0000-0000-000000000001" }
        }
        """);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* 测试清理失败忽略 */ }
    }
}
