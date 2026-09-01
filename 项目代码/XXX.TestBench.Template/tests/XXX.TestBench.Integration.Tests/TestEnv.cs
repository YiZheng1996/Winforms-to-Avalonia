namespace XXX.TestBench.Integration.Tests;

/// <summary>集成测试环境：在临时目录写 config 样例并给出数据目录。</summary>
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
          "schemaVersion": 1,
          "deviceMode": "{{deviceMode}}",
          "pollIntervalMs": 500,
          "timeoutMs": 1000,
          "devices": [ { "name": "SampleDevice", "protocol": "Simulation", "address": "sim://sample", "enabled": true } ]
        }
        """);
        File.WriteAllText(Path.Combine(ConfigRoot, "points.json"), """
        {
          "schemaVersion": 1,
          "points": [
            { "code": "AI_Pressure", "protocol": "Simulation", "address": "sim.pressure", "dataType": "Decimal", "unit": "MPa", "isWritable": false, "riskLevel": "Normal" },
            { "code": "DO_Start", "protocol": "Simulation", "address": "sim.start", "dataType": "Boolean", "isWritable": true, "riskLevel": "HighRisk" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(ConfigRoot, "simulation.json"), """
        {
          "schemaVersion": 1,
          "initialValues": { "sim.pressure": 0.0, "sim.start": false },
          "changeRules": [ { "address": "sim.pressure", "pattern": "ramp", "ratePerSecond": 0.1, "max": 10.0, "min": 0.0 } ],
          "faultInjectionScenarios": []
        }
        """);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* 测试清理失败忽略 */ }
    }
}
