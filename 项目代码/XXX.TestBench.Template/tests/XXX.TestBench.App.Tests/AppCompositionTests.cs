using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Domain.Devices;
using Xunit;

namespace XXX.TestBench.App.Tests;

public class AppCompositionTests
{
    [Fact]
    public void Create_WithValidConfig_BuildsShellAndServices()
    {
        var root = Path.Combine(Path.GetTempPath(), "testbench-app-" + Guid.NewGuid().ToString("N")[..8]);
        var configRoot = Path.Combine(root, "config");
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        try
        {
            File.WriteAllText(Path.Combine(configRoot, "app.json"), """
            {
              "schemaVersion": 1,
              "systemName": "测试系统",
              "brand": "XXX.TestBench.Template",
              "language": "zh-CN",
              "modules": { "processMonitor": true, "deviceCalibration": true },
              "defaultPaths": { "database": "testbench.db", "reportOutput": "reports", "reportTemplates": "assets/report-templates" }
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "device.json"), """
            {
              "schemaVersion": 3,
              "pollIntervalMs": 500,
              "timeoutMs": 1000,
              "channels": [
                { "id": "10000000-0000-5000-8000-000000000001", "code": "CH_PLC", "name": "PLC 网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": { "host": "127.0.0.1", "port": 102 } }
              ],
              "devices": [
                { "id": "20000000-0000-5000-8000-000000000001", "code": "DEV_SAMPLE", "name": "SampleDevice", "deviceMode": "Simulation", "protocol": "SiemensS7", "address": "127.0.0.1", "channelId": "10000000-0000-5000-8000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500 }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "points.json"), """
            {
              "schemaVersion": 3,
              "groups": [
                { "id": "30000000-0000-5000-8000-000000000001", "deviceId": "20000000-0000-5000-8000-000000000001", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 }
              ],
              "points": [
                { "id": "30000000-0000-5000-8000-000000000002", "code": "AI_Pressure", "name": "压力", "protocol": "SiemensS7", "deviceId": "20000000-0000-5000-8000-000000000001", "groupId": "30000000-0000-5000-8000-000000000001", "address": "DB1.DBD0", "dataType": "Float32", "rawDataType": "Float32", "isWritable": false, "riskLevel": "Normal" }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
            { "schemaVersion": 2, "initialValues": {}, "initialValuesByPointId": { "30000000-0000-5000-8000-000000000002": 0.0 }, "changeRules": [], "faultInjectionScenarios": [] }
            """);

            var composition = AppComposition.Create(configRoot, dataRoot);

            Assert.Null(composition.StartupError);
            Assert.NotNull(composition.Shell);
            Assert.False(composition.Shell!.IsFaulted);
            Assert.Equal("仿真模式", composition.Shell.DeviceModeText);
            Assert.NotNull(composition.Authentication);
            Assert.NotNull(composition.DeviceModes);
            Assert.Equal(DeviceHealth.Healthy, composition.DeviceModes!.Health);
            Assert.True(File.Exists(Path.Combine(dataRoot, "testbench.db")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Create_WithV2PointsConfig_PreservesSignalBindingsInRuntime()
    {
        var root = Path.Combine("D:\\Codex相关\\多设备点位改造", "app-v2-" + Guid.NewGuid().ToString("N")[..8]);
        var configRoot = Path.Combine(root, "config");
        var dataRoot = Path.Combine(root, "data");
        const string channelId = "10000000-0000-5000-8000-000000000001";
        const string deviceId = "20000000-0000-5000-8000-000000000001";
        const string pointId = "30000000-0000-5000-8000-000000000001";
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        try
        {
            File.WriteAllText(Path.Combine(configRoot, "app.json"), """
            {
              "schemaVersion": 1,
              "systemName": "测试系统",
              "brand": "XXX.TestBench.Template",
              "language": "zh-CN",
              "modules": { "processMonitor": true, "deviceCalibration": true },
              "defaultPaths": { "database": "testbench.db", "reportOutput": "reports", "reportTemplates": "assets/report-templates" }
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "device.json"), $$"""
            {
              "schemaVersion": 3,
              "pollIntervalMs": 500,
              "timeoutMs": 1000,
              "channels": [
                {
                  "id": "{{channelId}}",
                  "code": "CH_PLC",
                  "name": "PLC 网络通道",
                  "transportKind": "Tcp",
                  "enabled": true,
                  "timeoutMs": 1000,
                  "retryCount": 0,
                  "tcp": { "host": "127.0.0.1", "port": 102 }
                }
              ],
              "devices": [
                {
                  "id": "{{deviceId}}",
                  "code": "PLC1",
                  "name": "测试设备",
                  "deviceMode": "Simulation",
                  "protocol": "SiemensS7",
                  "address": "127.0.0.1",
                  "channelId": "{{channelId}}",
                  "driverKey": "siemens-s7",
                  "model": "S7-1500",
                  "pollIntervalMs": 500,
                  "staleAfterMs": 2000
                }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "points.json"), $$"""
            {
              "schemaVersion": 2,
              "points": [
                {
                  "id": "{{pointId}}",
                  "code": "AI_Pressure",
                  "name": "压力",
                  "deviceId": "{{deviceId}}",
                  "protocol": "SiemensS7",
                  "address": "DB1.DBD0",
                  "dataType": "Decimal",
                  "rawDataType": "Decimal",
                  "isWritable": false,
                  "riskLevel": "Normal"
                }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "simulation.json"), $$"""
            {
              "schemaVersion": 2,
              "initialValues": {},
              "initialValuesByPointId": { "{{pointId}}": 1.5 },
              "changeRules": [],
              "faultInjectionScenarios": []
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "signal-bindings.json"), $$"""
            {
              "schemaVersion": 1,
              "bindings": { "AI_Pressure": "{{pointId}}" }
            }
            """);

            var composition = AppComposition.Create(configRoot, dataRoot);

            Assert.Null(composition.StartupError);
            Assert.NotNull(composition.DeviceModes?.Runtime);
            Assert.Equal(pointId, composition.DeviceModes!.Runtime!.SignalBindings.Bindings["AI_Pressure"]);
            Assert.StartsWith("migration-", composition.DeviceModes.Runtime.ActiveRevision, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Create_WithInvalidConfig_EntersFaultedWithDiagnostic()
    {
        var root = Path.Combine(Path.GetTempPath(), "testbench-app-" + Guid.NewGuid().ToString("N")[..8]);
        var configRoot = Path.Combine(root, "config");
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(configRoot);
        try
        {
            File.WriteAllText(Path.Combine(configRoot, "app.json"), """
            {
              "schemaVersion": 99,
              "systemName": "x",
              "brand": "b",
              "language": "zh-CN",
              "defaultPaths": { "database": "t.db", "reportOutput": "r", "reportTemplates": "t" }
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "device.json"), """
            { "schemaVersion": 1, "deviceMode": "Simulation", "pollIntervalMs": 500, "timeoutMs": 1000, "devices": [] }
            """);
            File.WriteAllText(Path.Combine(configRoot, "points.json"), """
            { "schemaVersion": 1, "points": [] }
            """);
            File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
            { "schemaVersion": 1, "initialValues": {}, "changeRules": [], "faultInjectionScenarios": [] }
            """);

            var composition = AppComposition.Create(configRoot, dataRoot);

            Assert.NotNull(composition.StartupError);
            Assert.NotNull(composition.Shell);
            Assert.True(composition.Shell!.IsFaulted);
            Assert.Contains("schemaVersion", composition.Shell.FaultMessage);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
