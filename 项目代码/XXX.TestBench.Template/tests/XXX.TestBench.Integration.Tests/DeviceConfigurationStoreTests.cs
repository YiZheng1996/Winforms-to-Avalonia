using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Infrastructure.Configuration;
using System.Text.Json;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public sealed class DeviceConfigurationStoreTests
{
    [Fact]
    public async Task StageCommitAndRecoveryUseCompleteHashedRevisions()
    {
        var root = Path.Combine("D:\\Codex相关\\多设备点位改造", "store-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var store = new DeviceConfigurationStore(root);
            var first = CreateSnapshot("revision-one", 1d);
            await store.StageAsync(first);
            await store.CommitActiveAsync(first.Revision);

            var loadedFirst = await store.LoadActiveAsync();
            Assert.Equal(first.Revision, loadedFirst.Revision);
            var loadedValue = loadedFirst.Simulation.InitialValuesByPointId.Values.Single();
            Assert.Equal(1d, loadedValue is JsonElement element ? element.GetDouble() : Convert.ToDouble(loadedValue));

            var second = CreateSnapshot("revision-two", 2d, first.Points.Points[0].Id, first.Device.Devices[0].Id, first.Device.Channels[0].Id);
            await store.StageAsync(second);
            await store.CommitActiveAsync(second.Revision);

            var secondPoints = Path.Combine(root, "device-config", "revisions", second.Revision, "points.json");
            await File.AppendAllTextAsync(secondPoints, "\ncorrupted");

            var recovered = await store.LoadActiveAsync();
            Assert.Equal(first.Revision, recovered.Revision);
            Assert.Contains("上一版本", store.LastRecoveryMessage, StringComparison.Ordinal);
            await Assert.ThrowsAsync<XXX.TestBench.Core.Configuration.ConfigValidationException>(
                () => store.LoadRevisionAsync(second.Revision));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static DeviceConfigurationSnapshot CreateSnapshot(
        string revision,
        double value,
        string? pointId = null,
        string? deviceId = null,
        string? channelId = null)
    {
        var channel = new ChannelEntry
        {
            Id = channelId ?? Guid.NewGuid().ToString("D"),
            Code = "SIM",
            Name = "仿真通道",
            TransportKind = ChannelTransportKind.Simulation,
            Simulation = new SimulationChannelParameters { InstanceKey = "store-test" }
        };
        var device = new DeviceConfig.DeviceEntry
        {
            Id = deviceId ?? Guid.NewGuid().ToString("D"),
            Code = "SIM1",
            Name = "仿真设备",
            ChannelId = channel.Id,
            DriverKey = DriverKeyCatalog.Simulation,
            PollIntervalMs = 500,
            StaleAfterMs = 2000,
            Enabled = true
        };
        var group = new PointsConfig.PointGroupEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            DeviceId = device.Id,
            Code = "DEFAULT",
            Name = "未分组"
        };
        var point = new PointsConfig.PointEntry
        {
            Id = pointId ?? Guid.NewGuid().ToString("D"),
            Code = "AI_PRESSURE",
            Name = "压力",
            DeviceId = device.Id,
            GroupId = group.Id,
            Address = "sim.pressure",
            DataType = "Float32",
            RawDataType = "Float32",
            AddressDefinition = new PointAddressDefinition { LogicalAddress = "sim.pressure" }
        };
        var simulation = new SimulationConfig
        {
            SchemaVersion = SimulationConfig.CurrentSchemaVersion
        };
        simulation.InitialValuesByPointId[point.Id] = value;
        return new DeviceConfigurationSnapshot
        {
            Revision = revision,
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                DeviceMode = DeviceMode.Simulation,
                Channels = new List<ChannelEntry> { channel },
                Devices = new List<DeviceConfig.DeviceEntry> { device }
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = new List<PointsConfig.PointGroupEntry> { group },
                Points = new List<PointsConfig.PointEntry> { point }
            },
            Simulation = simulation,
            SignalBindings = new SignalBindingsConfig()
        };
    }
}
