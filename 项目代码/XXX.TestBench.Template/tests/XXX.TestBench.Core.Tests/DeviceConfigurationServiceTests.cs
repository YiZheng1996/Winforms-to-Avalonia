using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public sealed class DeviceConfigurationServiceTests
{
    [Fact]
    public async Task ApplyStagesThenCommitsOnlyAfterOperationGate()
    {
        var store = new FakeConfigurationStore();
        await using var operations = new DeviceOperationCoordinator();
        var service = CreateService(store, operations, new FakeAuditLog());

        var result = await service.ApplyAsync(TestContexts.Admin(), CreateSnapshot("r1"), new());

        Assert.True(result.Ok, result.Error);
        Assert.Equal("r1", store.Staged?.Revision);
        Assert.Equal("r1", store.ActiveRevision);
    }

    [Fact]
    public async Task CandidateRuntimeCreationFailureKeepsOldRevisionAndRestartsOldRuntime()
    {
        var store = new FakeConfigurationStore();
        var oldSnapshot = CreateSnapshot("r-old");
        await store.StageAsync(oldSnapshot);
        await store.CommitActiveAsync(oldSnapshot.Revision);

        var oldRuntime = new FakeRuntime(isSimulation: false);
        var audit = new FakeAuditLog();
        var published = false;
        await using var operations = new DeviceOperationCoordinator();
        var service = new DeviceConfigurationService(
            store,
            operations,
            audit,
            drivers: Array.Empty<IDeviceDriverDescriptor>(),
            hasActiveRun: () => false,
            runtimeFactory: (_, _) => Task.FromException<IDeviceRuntime>(new InvalidOperationException("候选运行时启动失败")),
            currentRuntime: () => oldRuntime,
            publishRuntime: _ =>
            {
                published = true;
                return Task.CompletedTask;
            },
            requiredSignals: Array.Empty<RequiredSignal>(),
            markRuntimeFaulted: _ => { });

        var result = await service.ApplyAsync(TestContexts.Admin(), CreateSnapshot("r-new"), new());

        Assert.False(result.Ok);
        Assert.Equal("r-old", store.ActiveRevision);
        Assert.Equal("r-old", (await store.LoadActiveAsync()).Revision);
        Assert.Equal(1, oldRuntime.StopCount);
        Assert.Equal(1, oldRuntime.StartCount);
        Assert.False(published);
        Assert.Contains(audit.Entries, entry => entry.Contains("DeviceConfigurationApplyFailed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ActiveRunAndZeroDevicesCannotBeApplied()
    {
        var store = new FakeConfigurationStore();
        await using var operations = new DeviceOperationCoordinator();
        var activeService = CreateService(store, operations, new FakeAuditLog(), hasActiveRun: true);

        var activeResult = await activeService.ApplyAsync(TestContexts.Admin(), CreateSnapshot("active"), new());
        Assert.False(activeResult.Ok);
        Assert.Contains("活动试验", activeResult.Error, StringComparison.Ordinal);
        Assert.Null(store.Staged);

        var emptySnapshot = CreateSnapshot("empty");
        emptySnapshot.Device.Devices.Clear();
        var emptyService = CreateService(store, operations, new FakeAuditLog());
        var emptyResult = await emptyService.ApplyAsync(TestContexts.Admin(), emptySnapshot, new());
        Assert.False(emptyResult.Ok);
        Assert.Contains("没有设备", emptyResult.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyDeviceConfigurationAsync_PreservesOtherSnapshotSections()
    {
        var initial = CreateSnapshot("initial");
        var store = new FakeConfigurationStore();
        await store.StageAsync(initial);
        await store.CommitActiveAsync(initial.Revision);
        await using var operations = new DeviceOperationCoordinator();
        var service = CreateService(store, operations, new FakeAuditLog());
        var sourceDevice = initial.Device.Devices[0];
        var replacement = new DeviceConfig
        {
            SchemaVersion = DeviceConfig.CurrentSchemaVersion,
            Channels = initial.Device.Channels,
            Devices =
            [
                new DeviceConfig.DeviceEntry
                {
                    Id = sourceDevice.Id,
                    Code = sourceDevice.Code,
                    Name = "改名后的仿真设备",
                    DeviceMode = sourceDevice.DeviceMode,
                    ChannelId = sourceDevice.ChannelId,
                    DriverKey = sourceDevice.DriverKey,
                    PollIntervalMs = sourceDevice.PollIntervalMs,
                    StaleAfterMs = sourceDevice.StaleAfterMs,
                    SiemensS7 = sourceDevice.SiemensS7
                }
            ]
        };

        var result = await service.ApplyDeviceConfigurationAsync(TestContexts.Admin(), replacement, new());

        Assert.True(result.Ok, result.Error);
        Assert.Equal("改名后的仿真设备", result.Snapshot!.Device.Devices[0].Name);
        Assert.Same(initial.Points, result.Snapshot.Points);
        Assert.Same(initial.Simulation, result.Snapshot.Simulation);
        Assert.Same(initial.SignalBindings, result.Snapshot.SignalBindings);
        Assert.Equal(result.Revision, store.ActiveRevision);
    }

    [Fact]
    public async Task ManageDevicesPermissionIsEnforcedInService()
    {
        await using var operations = new DeviceOperationCoordinator();
        var service = CreateService(new FakeConfigurationStore(), operations, new FakeAuditLog());

        await Assert.ThrowsAsync<XXX.TestBench.Core.Common.AuthorizationException>(() =>
            service.ApplyAsync(TestContexts.With(PermissionCode.ViewRecords), CreateSnapshot("r1"), new()));
    }

    private static DeviceConfigurationService CreateService(
        IDeviceConfigurationStore store,
        DeviceOperationCoordinator operations,
        IAuditLog audit,
        bool hasActiveRun = false)
        => new(
            store,
            operations,
            audit,
            drivers: Array.Empty<IDeviceDriverDescriptor>(),
            hasActiveRun: () => hasActiveRun,
            runtimeFactory: (_, _) => Task.FromResult<IDeviceRuntime>(new FakeRuntime(isSimulation: true)),
            currentRuntime: () => null,
            publishRuntime: _ => Task.CompletedTask,
            requiredSignals: Array.Empty<RequiredSignal>(),
            markRuntimeFaulted: _ => { });

    private static DeviceConfigurationSnapshot CreateSnapshot(string revision)
    {
        var channel = new ChannelEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "SIM",
            Name = "仿真通道",
            TransportKind = ChannelTransportKind.Tcp,
            Tcp = new TcpChannelParameters()
        };
        var device = new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "SIM1",
            Name = "仿真设备",
            ChannelId = channel.Id,
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1500",
            DeviceMode = DeviceMode.Simulation,
            PollIntervalMs = 500,
            StaleAfterMs = 2000,
            SiemensS7 = new SiemensS7ConnectionOptions { Host = "127.0.0.1", Port = 102, Rack = 0, Slot = 0 }
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
            Id = Guid.NewGuid().ToString("D"),
            Code = "P1",
            Name = "点位",
            DeviceId = device.Id,
            GroupId = group.Id,
            Protocol = "SiemensS7",
            Address = "DB1.DBD0",
            DataType = "Float32",
        };
        return new DeviceConfigurationSnapshot
        {
            Revision = revision,
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                Channels = new List<ChannelEntry> { channel },
                Devices = new List<DeviceConfig.DeviceEntry> { device }
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = new List<PointsConfig.PointGroupEntry> { group },
                Points = new List<PointsConfig.PointEntry> { point }
            },
            Simulation = new SimulationConfig { SchemaVersion = SimulationConfig.CurrentSchemaVersion },
            SignalBindings = new SignalBindingsConfig()
        };
    }

    private sealed class FakeConfigurationStore : IDeviceConfigurationStore
    {
        private readonly Dictionary<string, DeviceConfigurationSnapshot> _snapshots = new(StringComparer.Ordinal);
        public DeviceConfigurationSnapshot? Staged { get; private set; }
        public string? ActiveRevision { get; private set; }

        public Task<DeviceConfigurationSnapshot> LoadActiveAsync(CancellationToken ct = default)
            => Task.FromResult(ActiveRevision is not null && _snapshots.TryGetValue(ActiveRevision, out var active)
                ? active
                : throw new InvalidOperationException("没有快照"));

        public Task<DeviceConfigurationSnapshot> LoadPreviousAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("没有上一版本快照");

        public Task<DeviceConfigurationSnapshot> LoadRevisionAsync(string revision, CancellationToken ct = default)
            => Task.FromResult(_snapshots.TryGetValue(revision, out var snapshot)
                ? snapshot
                : throw new InvalidOperationException("没有快照"));

        public Task StageAsync(DeviceConfigurationSnapshot snapshot, CancellationToken ct = default)
        {
            Staged = snapshot;
            _snapshots[snapshot.Revision] = snapshot;
            return Task.CompletedTask;
        }

        public Task CommitActiveAsync(string revision, CancellationToken ct = default)
        {
            if (!_snapshots.ContainsKey(revision))
                throw new InvalidOperationException("没有可提交的候选快照");
            ActiveRevision = revision;
            return Task.CompletedTask;
        }
    }
}
