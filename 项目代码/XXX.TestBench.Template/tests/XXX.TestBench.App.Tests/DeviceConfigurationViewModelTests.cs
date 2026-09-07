using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.Tests;

public sealed class DeviceConfigurationViewModelTests
{
    [Fact]
    public void ChannelEditor_BuildsTcpParametersAndOnlySelectedTransport()
    {
        var tcp = new ChannelTransportChoice(ChannelTransportKind.Tcp, "TCP", "网络通道");
        var editor = new ChannelEditorViewModel(null, [tcp], "CH_TEST")
        {
            SelectedTransportOption = tcp,
            TcpHost = "127.0.0.1",
            TcpPortText = "502",
            TimeoutMsText = "1000",
            RetryCountText = "0"
        };

        var ok = editor.TryBuild(out var channel);

        Assert.True(ok, editor.ValidationMessage);
        Assert.Equal(ChannelTransportKind.Tcp, channel.TransportKind);
        Assert.Equal("127.0.0.1", channel.Tcp!.Host);
        Assert.Equal(502, channel.Tcp.Port);
        Assert.Null(channel.Serial);
        Assert.Null(channel.Simulation);
    }

    [Fact]
    public void DeviceEditor_RejectsEnabledUnsupportedDriver()
    {
        var channel = CreateSimulationChannel();
        var editor = new DeviceEditorViewModel(
            null,
            [channel],
            [new UnsupportedDescriptor(DriverKeyCatalog.ModbusTcp, "Modbus TCP")],
            "DEV_TEST");
        editor.SelectedDriver = editor.DriverOptions.First(option =>
            string.Equals(option.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase));
        editor.ModbusUnitIdText = "1";

        var ok = editor.TryBuild(out _);

        Assert.False(ok);
        Assert.Contains("未实现驱动", editor.ValidationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationEditor_DoesNotDeleteDeviceReferencedByPoint()
    {
        var channel = CreateSimulationChannel();
        var device = new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "DEV_1",
            Name = "仿真设备",
            ChannelId = channel.Id,
            DriverKey = DriverKeyCatalog.Simulation,
            PollIntervalMs = 500,
            StaleAfterMs = 2000,
            Enabled = true
        };
        var point = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "P1",
            Name = "压力",
            DeviceId = device.Id,
            GroupId = "40000000-0000-5000-8000-000000000001",
            Address = "sim.pressure",
            DataType = "Decimal",
            RawDataType = "Decimal",
            AddressDefinition = new PointAddressDefinition { LogicalAddress = "sim.pressure" }
        };
        var snapshot = new DeviceConfigurationSnapshot
        {
            Revision = "editor-test",
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                DeviceMode = DeviceMode.Simulation,
                Channels = [channel],
                Devices = [device]
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups =
                [
                    new PointsConfig.PointGroupEntry
                    {
                        Id = "40000000-0000-5000-8000-000000000001",
                        DeviceId = device.Id,
                        Code = "DEFAULT",
                        Name = "未分组"
                    }
                ],
                Points = [point]
            },
            Simulation = new SimulationConfig { SchemaVersion = SimulationConfig.CurrentSchemaVersion },
            SignalBindings = new SignalBindingsConfig()
        };
        var store = new FakeConfigurationStore(snapshot);
        var service = new DeviceConfigurationService(store, new DeviceOperationCoordinator(), new TestAuditLog());
        var editor = new DeviceConfigurationEditorViewModel(
            snapshot.Device, snapshot.Points.Points, snapshot.SignalBindings,
            null, service, Admin());

        editor.SelectedDevice = editor.Devices.Single();
        editor.DeleteSelectedDevice();

        Assert.Single(editor.Devices);
        Assert.Contains("仍被 1 个点位", editor.ValidationMessage, StringComparison.Ordinal);
    }

    private static ChannelEntry CreateSimulationChannel()
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "SIM",
            Name = "仿真通道",
            TransportKind = ChannelTransportKind.Simulation,
            Simulation = new SimulationChannelParameters { InstanceKey = "app-test" }
        };

    private static UserContext Admin()
    {
        var role = new Role { Name = "Administrator" };
        role.Permissions.Add(PermissionCode.ManageDevices);
        return new UserContext { LoginName = "admin", DisplayName = "管理员", Role = role };
    }

    private sealed class UnsupportedDescriptor(string driverKey, string displayName) : IDeviceDriverDescriptor
    {
        public string DriverKey { get; } = driverKey;
        public string DisplayName { get; } = displayName;
        public bool IsImplemented => false;
        public IReadOnlySet<DevicePointDataType> SupportedDataTypes { get; } = new HashSet<DevicePointDataType>();
        public IReadOnlyList<DriverValidationIssue> ValidateChannel(ChannelEntry channel) => [];
        public IReadOnlyList<DriverValidationIssue> ValidateDevice(DeviceConfig.DeviceEntry device, ChannelEntry channel) => [];
        public IReadOnlyList<DriverValidationIssue> ValidatePoint(PointsConfig.PointEntry point, DeviceConfig.DeviceEntry device) => [];
        public string NormalizeAddress(PointsConfig.PointEntry point) => point.Address;
    }

    private sealed class FakeConfigurationStore(DeviceConfigurationSnapshot snapshot) : IDeviceConfigurationStore
    {
        private DeviceConfigurationSnapshot _snapshot = snapshot;
        public Task<DeviceConfigurationSnapshot> LoadActiveAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
        public Task<DeviceConfigurationSnapshot> LoadRevisionAsync(string revision, CancellationToken ct = default) => Task.FromResult(_snapshot);
        public Task StageAsync(DeviceConfigurationSnapshot value, CancellationToken ct = default)
        {
            _snapshot = value;
            return Task.CompletedTask;
        }
        public Task CommitActiveAsync(string revision, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestAuditLog : IAuditLog
    {
        public Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
        public Task<IReadOnlyList<AuditEntry>> SearchAsync(AuditLogQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
    }
}
