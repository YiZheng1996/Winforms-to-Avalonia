using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Integration.Tests;

public sealed class DeviceConfigurationBootstrapperTests
{
    [Fact]
    public async Task FirstLoadMigratesLegacyFilesAndNextLoadPrefersActiveRevision()
    {
        var root = Path.Combine("D:\\Codex相关\\多设备点位改造", "bootstrap-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "device.json"), """
            {
              "schemaVersion": 1,
              "deviceMode": "Simulation",
              "pollIntervalMs": 500,
              "timeoutMs": 1000,
              "devices": [ { "name": "LegacySim", "protocol": "Simulation", "address": "sim://legacy", "enabled": true } ]
            }
            """);
            File.WriteAllText(Path.Combine(root, "points.json"), """
            {
              "schemaVersion": 1,
              "points": [ { "code": "P1", "name": "压力", "protocol": "Simulation", "address": "sim.pressure", "dataType": "Decimal", "isWritable": false, "riskLevel": "Normal" } ]
            }
            """);
            File.WriteAllText(Path.Combine(root, "simulation.json"), """
            {
              "schemaVersion": 1,
              "initialValues": { "sim.pressure": 1.5 },
              "changeRules": [],
              "faultInjectionScenarios": []
            }
            """);

            var legacyStore = new JsonConfigStore(root);
            await using var versionedStore = new DeviceConfigurationStore(root);
            var bootstrapper = new DeviceConfigurationBootstrapper(root, legacyStore, versionedStore);

            var first = await bootstrapper.LoadAsync();
            Assert.True(first.WasMigrated);
            Assert.True(File.Exists(Path.Combine(root, "device-config", "active.json")));
            Assert.Equal(DeviceConfig.CurrentSchemaVersion, first.Snapshot.Device.SchemaVersion);
            Assert.Equal(PointsConfig.CurrentSchemaVersion, first.Snapshot.Points.SchemaVersion);
            var firstRevision = first.Snapshot.Revision;
            Assert.All(first.Snapshot.Points.Points, point => Assert.True(Guid.TryParse(point.Id, out _)));

            File.WriteAllText(Path.Combine(root, "device.json"), "not used after active revision");
            var second = await bootstrapper.LoadAsync();

            Assert.False(second.WasMigrated);
            Assert.Equal(firstRevision, second.Snapshot.Revision);
            Assert.Equal("P1", Assert.Single(second.Snapshot.Points.Points).Code);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
