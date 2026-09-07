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
              "schemaVersion": 1,
              "deviceMode": "Simulation",
              "pollIntervalMs": 500,
              "timeoutMs": 1000,
              "devices": [ { "name": "SampleDevice", "protocol": "Simulation", "address": "sim://sample", "enabled": true } ]
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "points.json"), """
            {
              "schemaVersion": 1,
              "points": [
                { "code": "AI_Pressure", "protocol": "Simulation", "address": "sim.pressure", "dataType": "Decimal", "unit": "MPa", "isWritable": false, "riskLevel": "Normal" }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
            { "schemaVersion": 1, "initialValues": { "sim.pressure": 0.0 }, "changeRules": [], "faultInjectionScenarios": [] }
            """);

            var composition = AppComposition.Create(configRoot, dataRoot);

            Assert.Null(composition.StartupError);
            Assert.NotNull(composition.Shell);
            Assert.False(composition.Shell!.IsFaulted);
            Assert.Equal("Simulation（仿真）", composition.Shell.DeviceModeText);
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
    public void Create_WithV2Config_PreservesSignalBindingsInRuntime()
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
              "schemaVersion": 2,
              "deviceMode": "Simulation",
              "pollIntervalMs": 500,
              "timeoutMs": 1000,
              "channels": [
                {
                  "id": "{{channelId}}",
                  "code": "SIM",
                  "name": "仿真通道",
                  "transportKind": "Simulation",
                  "enabled": true,
                  "timeoutMs": 1000,
                  "retryCount": 0,
                  "simulation": { "instanceKey": "app-v2-test" }
                }
              ],
              "devices": [
                {
                  "id": "{{deviceId}}",
                  "code": "PLC1",
                  "name": "仿真设备",
                  "channelId": "{{channelId}}",
                  "driverKey": "simulation",
                  "manufacturer": "测试",
                  "model": "Simulation",
                  "pollIntervalMs": 500,
                  "staleAfterMs": 2000,
                  "enabled": true
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
                  "address": "sim.pressure",
                  "dataType": "Decimal",
                  "rawDataType": "Decimal",
                  "addressDefinition": { "logicalAddress": "sim.pressure" },
                  "unit": "MPa",
                  "isWritable": false,
                  "isEnabled": true,
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
