using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Drivers;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public sealed class S7OptimizedBlockAccessIntegrationTests
{
    [Fact]
    public async Task ApplyingS7HardwareDbAbsolutePoint_RequiresConfirmationBeforeStage()
    {
        var initial = CreateSnapshot("initial", "M10.1");
        var store = new FakeConfigurationStore(initial);
        await using var operations = new DeviceOperationCoordinator();
        var descriptor = new SiemensS7DriverDescriptor();
        var service = new DeviceConfigurationService(
            store,
            operations,
            new NoopAuditLog(),
            new[] { descriptor });
        var point = CreatePoint(initial, "DB1.DBD0");

        Assert.Empty(descriptor.ValidatePoint(point, initial.Device.Devices[0]));

        var unconfirmed = await service.ApplyPointsAsync(
            CreateAdmin(),
            new[] { point },
            initial.Points.Groups);

        Assert.False(unconfirmed.Ok);
        Assert.True(unconfirmed.RequiresS7OptimizedBlockAccessConfirmation);
        Assert.NotNull(unconfirmed.S7OptimizedBlockAccessNotice);
        Assert.Contains("DB1.DBD0", unconfirmed.Error, StringComparison.Ordinal);
        Assert.Contains("优化的块访问", unconfirmed.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("driverKey", unconfirmed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawDataType", unconfirmed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(store.Staged);

        var confirmed = await service.ApplyPointsAsync(
            CreateAdmin(),
            new[] { point },
            initial.Points.Groups,
            s7OptimizedBlockAccessConfirmed: true);

        Assert.True(confirmed.Ok, confirmed.Error);
        Assert.NotNull(store.Staged);
        Assert.Equal("DB1.DBD0", store.Staged!.Points.Points.Single().Address);
    }

    private static UserContext CreateAdmin()
    {
        var role = new Role { Name = "管理员" };
        role.Permissions.Add(PermissionCode.ManageDevices);
        return new UserContext
        {
            UserId = 1,
            LoginName = "admin",
            DisplayName = "管理员",
            Role = role
        };
    }

    private static DeviceConfigurationSnapshot CreateSnapshot(string revision, string address)
    {
        var channel = new ChannelEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "CH_S7",
            Name = "PLC 通道",
            TransportKind = ChannelTransportKind.Tcp,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 102 }
        };
        var device = new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "DEV_S7",
            Name = "测试 PLC",
            DeviceMode = DeviceMode.Hardware,
            ChannelId = channel.Id,
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1200",
            PollIntervalMs = 500,
            StaleAfterMs = 2000
        };
        var group = new PointsConfig.PointGroupEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            DeviceId = device.Id,
            Code = "DEFAULT",
            Name = "未分组"
        };
        var point = CreatePoint(device, group.Id, address);
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

    private static PointsConfig.PointEntry CreatePoint(
        DeviceConfigurationSnapshot snapshot,
        string address)
    {
        var device = snapshot.Device.Devices[0];
        var group = snapshot.Points.Groups[0];
        return CreatePoint(device, group.Id, address);
    }

    private static PointsConfig.PointEntry CreatePoint(
        DeviceConfig.DeviceEntry device,
        string groupId,
        string address)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "AI_PRESSURE",
            Name = "入口压力",
            DeviceId = device.Id,
            DeviceCode = device.Code,
            GroupId = groupId,
            GroupCode = "DEFAULT",
            Protocol = "SiemensS7",
            Address = address,
            DataType = "Float32",
            RawDataType = "Float32"
        };

    private sealed class FakeConfigurationStore : IDeviceConfigurationStore
    {
        public FakeConfigurationStore(DeviceConfigurationSnapshot active)
        {
            Active = active;
        }

        public DeviceConfigurationSnapshot Active { get; private set; }
        public DeviceConfigurationSnapshot? Staged { get; private set; }

        public Task<DeviceConfigurationSnapshot> LoadActiveAsync(CancellationToken ct = default)
            => Task.FromResult(Active);

        public Task<DeviceConfigurationSnapshot> LoadRevisionAsync(string revision, CancellationToken ct = default)
            => Task.FromResult(Active);

        public Task StageAsync(DeviceConfigurationSnapshot snapshot, CancellationToken ct = default)
        {
            Staged = snapshot;
            return Task.CompletedTask;
        }

        public Task CommitActiveAsync(string revision, CancellationToken ct = default)
        {
            if (Staged is not null)
                Active = Staged;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopAuditLog : IAuditLog
    {
        public Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>(Array.Empty<AuditEntry>());

        public Task<IReadOnlyList<AuditEntry>> SearchAsync(AuditLogQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>(Array.Empty<AuditEntry>());
    }
}
