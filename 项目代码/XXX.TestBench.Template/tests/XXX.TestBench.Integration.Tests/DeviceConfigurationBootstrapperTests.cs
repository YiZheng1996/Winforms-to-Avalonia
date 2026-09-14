using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;
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

    [Fact]
    public async Task InvalidActiveRevision_RebuildsFromCurrentSchemaFiles()
    {
        var root = Path.Combine("D:\\Codex相关\\设备点位按设备仿真", "bootstrap-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "device-config"));
        try
        {
            File.WriteAllText(Path.Combine(root, "device-config", "active.json"), "{}\n");
            File.WriteAllText(Path.Combine(root, "device.json"), """
            {
              "schemaVersion": 3,
              "pollIntervalMs": 500,
              "timeoutMs": 1000,
              "channels": [
                { "id": "10000000-0000-0000-0000-000000000001", "code": "CH_PLC", "name": "PLC网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": { "host": "127.0.0.1", "port": 102 } }
              ],
              "devices": [
                { "id": "20000000-0000-0000-0000-000000000001", "code": "DEV_CURRENT", "name": "当前设备", "deviceMode": "Simulation", "protocol": "SiemensS7", "address": "127.0.0.1", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 2000 }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(root, "points.json"), """
            {
              "schemaVersion": 3,
              "groups": [
                { "id": "30000000-0000-0000-0000-000000000001", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 }
              ],
              "points": [
                { "id": "40000000-0000-0000-0000-000000000001", "code": "P1", "name": "压力", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000001", "address": "DB1.DBD0", "dataType": "Float32", "rawDataType": "Float32" }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(root, "simulation.json"), """
            {
              "schemaVersion": 2,
              "initialValuesByPointId": {},
              "changeRules": [],
              "faultInjectionScenarios": []
            }
            """);

            var versionedStore = new RejectingActiveStore();
            var bootstrapper = new DeviceConfigurationBootstrapper(
                root,
                new JsonConfigStore(root),
                versionedStore);

            var result = await bootstrapper.LoadAsync();

            Assert.True(result.WasMigrated);
            Assert.Contains("重建", result.Message, StringComparison.Ordinal);
            Assert.Equal(DeviceConfig.CurrentSchemaVersion, result.Snapshot.Device.SchemaVersion);
            Assert.Equal("DEV_CURRENT", Assert.Single(result.Snapshot.Device.Devices).Code);
            Assert.Equal(result.Snapshot.Revision, versionedStore.CommittedRevision);
            Assert.Equal(result.Snapshot.Revision, versionedStore.Staged?.Revision);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class RejectingActiveStore : IDeviceConfigurationStore
    {
        public DeviceConfigurationSnapshot? Staged { get; private set; }
        public string? CommittedRevision { get; private set; }

        public Task<DeviceConfigurationSnapshot> LoadActiveAsync(CancellationToken ct = default)
            => throw new ConfigValidationException("旧 active 中的 device.json schemaVersion=2 不受支持");

        public Task<DeviceConfigurationSnapshot> LoadRevisionAsync(string revision, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task StageAsync(DeviceConfigurationSnapshot snapshot, CancellationToken ct = default)
        {
            Staged = snapshot;
            return Task.CompletedTask;
        }

        public Task CommitActiveAsync(string revision, CancellationToken ct = default)
        {
            CommittedRevision = revision;
            return Task.CompletedTask;
        }
    }
}
