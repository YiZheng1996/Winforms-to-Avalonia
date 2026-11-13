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
          "schemaVersion": 3,
          "pollIntervalMs": 500,
          "timeoutMs": 1000,
          "channels": [
            { "id": "10000000-0000-0000-0000-000000000001", "code": "CH_PLC", "name": "PLC 网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": { "host": "127.0.0.1", "port": 102 } }
          ],
          "devices": [
            { "id": "20000000-0000-0000-0000-000000000001", "code": "DEV_SAMPLE", "name": "SampleDevice", "deviceMode": "{{deviceMode}}", "protocol": "SiemensS7", "address": "127.0.0.1", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500 }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(ConfigRoot, "points.json"), """
        {
          "schemaVersion": 3,
          "groups": [
            { "id": "30000000-0000-0000-0000-000000000001", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 }
          ],
          "points": [
            { "id": "40000000-0000-0000-0000-000000000001", "code": "AI_Pressure", "name": "压力", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000001", "address": "DB1.DBD0", "dataType": "Float32", "rawDataType": "Float32", "isWritable": false, "riskLevel": "Normal" },
            { "id": "40000000-0000-0000-0000-000000000002", "code": "DO_Start", "name": "启动", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000001", "address": "DB1.DBX4.0", "dataType": "Boolean", "rawDataType": "Boolean", "writePolicy": "ReadBackEqual", "isWritable": true, "riskLevel": "HighRisk" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(ConfigRoot, "simulation.json"), """
        {
          "schemaVersion": 2,
          "initialValues": {},
          "initialValuesByPointId": {
            "40000000-0000-0000-0000-000000000001": 0.0,
            "40000000-0000-0000-0000-000000000002": false
          },
          "changeRules": [ { "pointId": "40000000-0000-0000-0000-000000000001", "address": "DB1.DBD0", "pattern": "ramp", "ratePerSecond": 0.1, "max": 10.0, "min": 0.0 } ],
          "faultInjectionScenarios": []
        }
        """);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* 测试清理失败忽略 */ }
    }
}
