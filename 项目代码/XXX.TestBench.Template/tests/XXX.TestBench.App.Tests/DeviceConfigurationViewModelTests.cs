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
    public void ChannelEditor_BuildsTargetlessTcpResourceAndOnlySelectedTransport()
    {
        var tcp = new ChannelTransportChoice(ChannelTransportKind.Tcp, "TCP", "网络通道");
        var editor = new ChannelEditorViewModel(null, [tcp], "CH_TEST")
        {
            SelectedTransportOption = tcp,
            TimeoutMsText = "1000",
            RetryCountText = "0"
        };

        var ok = editor.TryBuild(out var channel);

        Assert.True(ok, editor.ValidationMessage);
        Assert.Equal(ChannelTransportKind.Tcp, channel.TransportKind);
        Assert.Equal(string.Empty, channel.Tcp!.Host);
        Assert.Equal(0, channel.Tcp.Port);
        Assert.Null(channel.Serial);
        Assert.Null(channel.Simulation);
    }

    [Fact]
    public void ChannelEditorWizard_ValidatesEachStepAndBuildsOnStepFour()
    {
        var tcp = new ChannelTransportChoice(ChannelTransportKind.Tcp, "TCP", "网络通道");
        var editor = new ChannelEditorViewModel(null, [tcp], "CH_WIZARD")
        {
            SelectedTransportOption = tcp,
            Name = "现场以太网",
            TimeoutMsText = "1000",
            RetryCountText = "1"
        };

        Assert.Equal(1, editor.CurrentStep);
        Assert.Equal("第 1 步，共 4 步", editor.CurrentStepText);
        Assert.Equal("下一步", editor.PrimaryActionText);
        Assert.True(editor.CanProceed);
        Assert.False(editor.CanGoBack);

        Assert.True(editor.MoveNext());
        Assert.Equal(2, editor.CurrentStep);
        Assert.True(editor.IsStep1Done);
        Assert.True(editor.IsStep2);

        editor.Name = string.Empty;
        Assert.False(editor.MoveNext());
        Assert.Contains("通道名称不能为空", editor.ValidationMessage, StringComparison.Ordinal);
        editor.Name = "现场以太网";
        Assert.True(editor.MoveNext());
        Assert.Equal(3, editor.CurrentStep);
        Assert.True(editor.IsStep2Done);

        Assert.True(editor.MoveNext());
        Assert.Equal(4, editor.CurrentStep);
        Assert.Equal("创建通道", editor.PrimaryActionText);
        Assert.True(editor.CanGoBack);
        Assert.True(editor.CanSave);
        Assert.True(editor.CanProceed);

        editor.MoveBack();
        Assert.Equal(3, editor.CurrentStep);
        Assert.Equal("下一步", editor.PrimaryActionText);
    }

    [Fact]
    public void SerialChannelEditor_UsesClosedPortAndBaudRateOptions()
    {
        var serial = new ChannelTransportChoice(ChannelTransportKind.Serial, "串口", "串行通道");
        var editor = new ChannelEditorViewModel(null, [serial], "CH_SERIAL")
        {
            SelectedTransportOption = serial,
            Name = "串口仪表",
            TimeoutMsText = "1000",
            RetryCountText = "1",
            SerialPortName = "COM3",
            SelectedBaudRate = 9600
        };

        Assert.Contains("COM3", editor.SerialPortOptions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(9600, editor.BaudRateOptions);
        Assert.Contains(115200, editor.BaudRateOptions);
        Assert.True(editor.TryBuild(out var channel), editor.ValidationMessage);
        Assert.Equal("COM3", channel.Serial!.PortName);
        Assert.Equal(9600, channel.Serial.BaudRate);
    }

    [Fact]
    public void EditingSerialChannelKeepsAConfiguredPortAndNonStandardBaudRateInOptions()
    {
        var current = new ChannelEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "CH_SERIAL",
            Name = "现场串口",
            TransportKind = ChannelTransportKind.Serial,
            TimeoutMs = 1000,
            RetryCount = 0,
            Serial = new SerialChannelParameters
            {
                PortName = "COM77",
                BaudRate = 76800,
                DataBits = 8,
                Parity = "None",
                StopBits = "One"
            }
        };
        var serial = new ChannelTransportChoice(ChannelTransportKind.Serial, "串口", "串行通道");
        var editor = new ChannelEditorViewModel(current, [serial]);

        Assert.Equal("COM77", editor.SerialPortName);
        Assert.Contains("COM77", editor.SerialPortOptions, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(76800, editor.SelectedBaudRate);
        Assert.Contains(76800, editor.BaudRateOptions);
    }

    [Fact]
    public void DeviceEditorWizard_ValidatesEachStepAndBuildsOnStepThree()
    {
        var channel = CreateTcpChannel();
        var editor = new DeviceEditorViewModel(
            null,
            [channel],
            descriptors: null,
            defaultCode: "DEV_WIZARD",
            defaultChannelId: channel.Id);

        Assert.Equal(1, editor.CurrentStep);
        Assert.Equal("第 1 步，共 3 步", editor.CurrentStepText);
        Assert.Equal("下一步：通信参数", editor.PrimaryActionText);
        Assert.False(editor.CanGoBack);

        editor.Name = string.Empty;
        Assert.False(editor.MoveNext());
        Assert.Contains("设备名称不能为空", editor.ValidationMessage, StringComparison.Ordinal);

        editor.Name = "仿真设备";
        Assert.True(editor.MoveNext());
        Assert.Equal(2, editor.CurrentStep);
        Assert.True(editor.IsStep1Done);

        editor.PollIntervalMsText = string.Empty;
        Assert.False(editor.MoveNext());
        Assert.Contains("轮询周期", editor.ValidationMessage, StringComparison.Ordinal);

        editor.PollIntervalMsText = "500";
        Assert.True(editor.MoveNext());
        Assert.Equal(3, editor.CurrentStep);
        Assert.Equal("创建设备", editor.PrimaryActionText);
        Assert.True(editor.CanGoBack);
        Assert.True(editor.CanSave);
        Assert.True(editor.CanProceed);

        Assert.True(editor.TryBuild(out var device), editor.ValidationMessage);
        Assert.Equal("仿真设备", device.Name);
        Assert.Equal(500, device.PollIntervalMs);

        editor.MoveBack();
        Assert.Equal(2, editor.CurrentStep);
        Assert.Equal("下一步：确认保存", editor.PrimaryActionText);
    }

    [Fact]
    public void SeparateEditorsUseBusinessNamesAndKeepGeneratedIdentitiesHiddenFromTheForm()
    {
        var channel = CreateTcpChannel();
        var snapshot = new DeviceConfigurationSnapshot
        {
            Revision = "separate-editor-test",
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                DeviceMode = DeviceMode.Simulation,
                Channels = [channel],
                Devices = []
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = [],
                Points = []
            },
            Simulation = new SimulationConfig { SchemaVersion = SimulationConfig.CurrentSchemaVersion },
            SignalBindings = new SignalBindingsConfig()
        };
        var service = new DeviceConfigurationService(
            new FakeConfigurationStore(snapshot),
            new DeviceOperationCoordinator(),
            new TestAuditLog());
        var transaction = new DeviceConfigurationEditorViewModel(
            snapshot.Device,
            [],
            snapshot.SignalBindings,
            null,
            service,
            Admin());

        var channelForm = transaction.CreateChannelEditor()
            ?? throw new InvalidOperationException("通道编辑器未创建");
        Assert.False(channelForm.IsEdit);
        Assert.Equal("新增通道", channelForm.DialogTitle);
        Assert.DoesNotContain("编码", channelForm.DialogSubtitle, StringComparison.Ordinal);
        channelForm.Name = "PLC";
        Assert.True(channelForm.TryBuild(out var newChannel), channelForm.ValidationMessage);
        Assert.True(transaction.ApplyChannelEntry(newChannel), transaction.ValidationMessage);
        Assert.Equal("PLC", newChannel.Name);
        Assert.StartsWith("CH_", newChannel.Code, StringComparison.Ordinal);

        var deviceForm = transaction.CreateDeviceEditor(defaultChannelId: channel.Id)
            ?? throw new InvalidOperationException("设备编辑器未创建");
        Assert.False(deviceForm.IsEdit);
        Assert.Equal("新增设备", deviceForm.DialogTitle);
        deviceForm.Name = "主控设备";
        Assert.True(deviceForm.TryBuild(out var newDevice), deviceForm.ValidationMessage);
        Assert.True(transaction.ApplyDeviceEntry(newDevice), transaction.ValidationMessage);
        Assert.Equal("主控设备", newDevice.Name);
        Assert.StartsWith("DEV_", newDevice.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceEditor_RejectsUnsupportedDriverInHardwareMode()
    {
        var channel = CreateTcpChannel();
        var editor = new DeviceEditorViewModel(
            null,
            [channel],
            [new UnsupportedDescriptor(DriverKeyCatalog.ModbusTcp, "Modbus TCP")],
            "DEV_TEST");
        editor.SelectedDeviceMode = editor.DeviceModeOptions.Single(option => option.Value == DeviceMode.Hardware);
        editor.SelectedDriver = editor.DriverOptions.First(option =>
            string.Equals(option.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase));
        editor.ModbusUnitIdText = "1";

        var ok = editor.TryBuild(out _);

        Assert.False(ok);
        Assert.Contains("硬件通信尚未实现", editor.ValidationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceEditor_AllowsPhysicalDriverAndFixedModelInSimulationMode()
    {
        var channel = CreateTcpChannel();
        var editor = new DeviceEditorViewModel(
            null,
            [channel],
            [new UnsupportedDescriptor(DriverKeyCatalog.ModbusTcp, "Modbus TCP")],
            "DEV_TEST");
        editor.SelectedDriver = editor.DriverOptions.Single(option =>
            option.DriverKey == DriverKeyCatalog.ModbusTcp);
        editor.ModbusUnitIdText = "1";
        editor.SelectedDeviceMode = editor.DeviceModeOptions.Single(option => option.Value == DeviceMode.Simulation);

        Assert.Equal("通用设备", Assert.Single(editor.ModelOptions).DisplayName);
        Assert.True(editor.TryBuild(out var device), editor.ValidationMessage);
        Assert.Equal(DriverKeyCatalog.ModbusTcp, device.DriverKey);
        Assert.Equal("GENERIC", device.Model);
        Assert.Equal(DeviceMode.Simulation, device.DeviceMode);
        Assert.DoesNotContain(
            editor.DriverOptions,
            option => string.Equals(option.DriverKey, DriverKeyCatalog.Simulation, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConfigurationEditor_DoesNotDeleteDeviceReferencedByPoint()
    {
        var channel = CreateTcpChannel();
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

    private static ChannelEntry CreateTcpChannel()
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "TCP",
            Name = "TCP 网络通道",
            TransportKind = ChannelTransportKind.Tcp,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 1502 }
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
        public IReadOnlySet<ChannelTransportKind> SupportedTransports { get; } =
            new HashSet<ChannelTransportKind> { ChannelTransportKind.Tcp };
        public IReadOnlyList<DeviceModelDescriptor> DeviceModels { get; } =
            [new("GENERIC", "通用设备", "按设备手册填写", "按设备手册填写地址。")];
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
