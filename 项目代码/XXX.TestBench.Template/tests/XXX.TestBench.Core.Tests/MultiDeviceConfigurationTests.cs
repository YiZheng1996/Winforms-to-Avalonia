using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public sealed class MultiDeviceConfigurationTests
{
    [Fact]
    public void SameAddressOnDifferentDevices_IsValid()
    {
        var snapshot = CreateSnapshot();
        var device2 = snapshot.Device.Devices[1];
        snapshot.Points.Points.Add(new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "AI_PRESSURE_2",
            DeviceId = device2.Id,
            GroupId = snapshot.Points.Groups.Single(group => group.DeviceId == device2.Id).Id,
            Address = "40001",
            DataType = "Float32",
            RawDataType = "Float32",
            Protocol = "",
            AddressDefinition = new PointAddressDefinition { Area = "HoldingRegister", Offset = 0 }
        });

        var issues = MultiDeviceConfigurationValidator.Validate(snapshot);

        Assert.DoesNotContain(issues, issue => issue.Message.Contains("重复", StringComparison.Ordinal));
    }

    [Fact]
    public void SameAddressOnSameDevice_IsRejected()
    {
        var snapshot = CreateSnapshot();
        snapshot.Points.Points.Add(new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "AI_PRESSURE_DUP",
            DeviceId = snapshot.Device.Devices[0].Id,
            Address = "sim.pressure",
            DataType = "Float32",
            RawDataType = "Float32",
            Protocol = "",
            AddressDefinition = new PointAddressDefinition { LogicalAddress = "sim.pressure" }
        });

        var issues = MultiDeviceConfigurationValidator.Validate(snapshot);

        Assert.Contains(issues, issue => issue.Message.Contains("重复", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingDeviceReference_IsRejected()
    {
        var snapshot = CreateSnapshot();
        snapshot.Points.Points[0].DeviceId = Guid.NewGuid().ToString("D");

        var issues = MultiDeviceConfigurationValidator.Validate(snapshot);

        Assert.Contains(issues, issue => issue.Path.Contains("deviceId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PointGroupMustBelongToPointDevice()
    {
        var snapshot = CreateSnapshot();
        snapshot.Points.Points[0].GroupId = snapshot.Points.Groups
            .Single(group => group.DeviceId == snapshot.Device.Devices[1].Id).Id;

        var issues = MultiDeviceConfigurationValidator.Validate(snapshot);

        Assert.Contains(issues, issue => issue.Message.Contains("同一设备", StringComparison.Ordinal));
    }

    [Fact]
    public void EnabledSerialChannelCannotBeReused()
    {
        var snapshot = CreateSnapshot();
        snapshot.Device.Channels.Clear();
        snapshot.Device.Channels.Add(CreateSerialChannel("COM3"));
        snapshot.Device.Channels.Add(CreateSerialChannel("com3"));
        snapshot.Device.Devices[0].ChannelId = snapshot.Device.Channels[0].Id;
        snapshot.Device.Devices[1].ChannelId = snapshot.Device.Channels[1].Id;
        snapshot.Device.DeviceMode = DeviceMode.Hardware;
        snapshot.Device.Devices[0].DriverKey = DriverKeyCatalog.ModbusTcp;
        snapshot.Device.Devices[1].DriverKey = DriverKeyCatalog.ModbusTcp;

        var issues = MultiDeviceConfigurationValidator.Validate(snapshot);

        Assert.Contains(issues, issue => issue.Message.Contains("串口", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyMigrationIsDeterministicAndDoesNotGuessDecimalType()
    {
        var device = new DeviceConfig
        {
            SchemaVersion = DeviceConfig.LegacySchemaVersion,
            DeviceMode = DeviceMode.Simulation,
            PollIntervalMs = 500,
            TimeoutMs = 1000
        };
        device.Devices.Add(new DeviceConfig.DeviceEntry
        {
            Name = "旧仿真设备",
            Protocol = "Simulation",
            Address = "sim://legacy"
        });
        var points = new PointsConfig { SchemaVersion = PointsConfig.LegacySchemaVersion };
        points.Points.Add(new PointsConfig.PointEntry
        {
            Code = "AI_Pressure",
            Name = "压力",
            Protocol = "Simulation",
            Address = "sim.pressure",
            DataType = "Decimal"
        });
        var simulation = new SimulationConfig { SchemaVersion = SimulationConfig.LegacySchemaVersion };
        simulation.InitialValues["sim.pressure"] = 1.5m;

        var first = DeviceConfigurationMigrator.Migrate(device, points, simulation);
        var second = DeviceConfigurationMigrator.Migrate(device, points, simulation);

        Assert.True(first.IsValid, string.Join("；", first.Issues));
        Assert.Equal(first.Candidate.Revision, second.Candidate.Revision);
        Assert.Equal(first.Candidate.Device.Devices[0].Id, second.Candidate.Device.Devices[0].Id);
        Assert.Equal(first.Candidate.Points.Points[0].Id, second.Candidate.Points.Points[0].Id);
        Assert.Single(first.Candidate.Points.Groups);
        Assert.Equal(first.Candidate.Points.Groups[0].Id, first.Candidate.Points.Points[0].GroupId);
        Assert.Equal("Decimal", first.Candidate.Points.Points[0].RawDataType);
        Assert.NotEqual(DevicePointDataType.Float32, first.Candidate.Points.Points[0].RawDataTypeKind);
    }

    private static DeviceConfigurationSnapshot CreateSnapshot()
    {
        var channel = new ChannelEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "SIM",
            Name = "仿真通道",
            TransportKind = ChannelTransportKind.Simulation,
            Simulation = new SimulationChannelParameters { InstanceKey = "test" }
        };
        var device1 = new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "PLC1",
            Name = "PLC1",
            ChannelId = channel.Id,
            DriverKey = DriverKeyCatalog.Simulation,
            PollIntervalMs = 500,
            StaleAfterMs = 2000,
            Enabled = true
        };
        var device2 = new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "PLC2",
            Name = "PLC2",
            ChannelId = channel.Id,
            DriverKey = DriverKeyCatalog.Simulation,
            PollIntervalMs = 500,
            StaleAfterMs = 2000,
            Enabled = true
        };
        var point = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "AI_PRESSURE_1",
            DeviceId = device1.Id,
            GroupId = "40000000-0000-5000-8000-000000000001",
            Address = "sim.pressure",
            DataType = "Float32",
            RawDataType = "Float32",
            Protocol = "",
            AddressDefinition = new PointAddressDefinition { LogicalAddress = "sim.pressure" }
        };
        var simulation = new SimulationConfig
        {
            SchemaVersion = SimulationConfig.CurrentSchemaVersion
        };
        var group1 = new PointsConfig.PointGroupEntry
        {
            Id = "40000000-0000-5000-8000-000000000001",
            DeviceId = device1.Id,
            Code = "DEFAULT",
            Name = "未分组"
        };
        var group2 = new PointsConfig.PointGroupEntry
        {
            Id = "40000000-0000-5000-8000-000000000002",
            DeviceId = device2.Id,
            Code = "DEFAULT",
            Name = "未分组"
        };
        simulation.InitialValuesByPointId[point.Id] = 0.0;
        return new DeviceConfigurationSnapshot
        {
            Revision = "test-revision",
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                DeviceMode = DeviceMode.Simulation,
                Channels = new List<ChannelEntry> { channel },
                Devices = new List<DeviceConfig.DeviceEntry> { device1, device2 }
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = new List<PointsConfig.PointGroupEntry> { group1, group2 },
                Points = new List<PointsConfig.PointEntry> { point }
            },
            Simulation = simulation,
            SignalBindings = new SignalBindingsConfig()
        };
    }

    private static ChannelEntry CreateSerialChannel(string portName)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "SERIAL_" + portName,
            Name = "串口 " + portName,
            TransportKind = ChannelTransportKind.Serial,
            Serial = new SerialChannelParameters
            {
                PortName = portName,
                BaudRate = 9600,
                DataBits = 8,
                Parity = "None",
                StopBits = "One"
            }
        };
}
