using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public sealed class SiemensS7OptimizedBlockAccessAdvisorTests
{
    [Theory]
    [InlineData("DB1.DBD0")]
    [InlineData("DB1.DBW0")]
    [InlineData("DB1.DBB0")]
    [InlineData("DB1.DBX0.0")]
    [InlineData("%DB12.DBD4")]
    public void IsDbAbsoluteAddress_RecognizesClassicS7DbNotation(string address)
        => Assert.True(SiemensS7OptimizedBlockAccessAdvisor.IsDbAbsoluteAddress(address));

    [Theory]
    [InlineData("M10.1")]
    [InlineData("VW5022")]
    [InlineData("I0.0")]
    [InlineData("Q0.0")]
    [InlineData("DB1.DBX0")]
    [InlineData("DB1.DBX0.8")]
    [InlineData("sim.a.pressure")]
    [InlineData("")]
    public void IsDbAbsoluteAddress_DoesNotMisreportOtherAddresses(string address)
        => Assert.False(SiemensS7OptimizedBlockAccessAdvisor.IsDbAbsoluteAddress(address));

    [Fact]
    public void DisplayNotice_UsesCustomerFacingChineseHelp()
    {
        var notice = SiemensS7OptimizedBlockAccessAdvisor.CreateDisplayNotice(
            DriverKeyCatalog.SiemensS7,
            "DB1.DBD0");

        Assert.NotNull(notice);
        Assert.Contains("DB1.DBD0", notice.ShortMessage, StringComparison.Ordinal);
        Assert.Contains("优化的块访问", notice.ShortMessage, StringComparison.Ordinal);
        Assert.Contains("TIA Portal", notice.DetailMessage, StringComparison.Ordinal);
        Assert.Contains("取消勾选“优化的块访问”", notice.DetailMessage, StringComparison.Ordinal);
        Assert.Contains("重新下载 PLC", notice.ConfirmationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("driverKey", notice.ConfirmationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawDataType", notice.ConfirmationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsImplemented", notice.ConfirmationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_OnlyReportsHardwareS7DbAbsoluteRisk()
    {
        var device = CreateDevice(DeviceMode.Hardware);
        var point = CreatePoint(device, "DB1.DBD0");

        var notice = SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            new[] { point },
            new[] { device },
            requireHardwareMode: true);

        Assert.NotNull(notice);
        Assert.Contains("DB1.DBD0", notice.ShortMessage, StringComparison.Ordinal);
        Assert.Null(SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            new[] { point },
            new[] { CreateDevice(DeviceMode.Simulation) },
            requireHardwareMode: true));
        Assert.Null(SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            new[] { CreatePoint(device, "M10.1") },
            new[] { device },
            requireHardwareMode: true));
    }

    [Fact]
    public void Inspect_DoesNotUseDbNotationForNonS7Device()
    {
        var device = CreateDevice(DeviceMode.Hardware);
        device.DriverKey = DriverKeyCatalog.ModbusTcp;
        var point = CreatePoint(device, "DB1.DBD0");

        Assert.Null(SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            new[] { point },
            new[] { device },
            requireHardwareMode: true));
    }

    [Fact]
    public void Inspect_WithPreviousSnapshot_OnlyReportsNewOrChangedRisk()
    {
        var device = CreateDevice(DeviceMode.Hardware);
        var previous = CreateSnapshot(device, CreatePoint(device, "M10.1"));
        var candidate = CreateSnapshot(device, CreatePoint(device, "DB1.DBD0"));

        var changed = SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            candidate.Points.Points,
            candidate.Device.Devices,
            requireHardwareMode: true,
            previousPoints: previous.Points.Points,
            previousDevices: previous.Device.Devices);

        Assert.NotNull(changed);
        var unchanged = SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            candidate.Points.Points,
            candidate.Device.Devices,
            requireHardwareMode: true,
            previousPoints: candidate.Points.Points,
            previousDevices: candidate.Device.Devices);
        Assert.Null(unchanged);
    }

    [Fact]
    public void RequiredConfirmation_IsLimitedToHardwareMode()
    {
        Assert.True(SiemensS7OptimizedBlockAccessAdvisor.ShouldRequireConfirmation(
            DriverKeyCatalog.SiemensS7,
            DeviceMode.Hardware,
            "DB1.DBW0"));
        Assert.False(SiemensS7OptimizedBlockAccessAdvisor.ShouldRequireConfirmation(
            DriverKeyCatalog.SiemensS7,
            DeviceMode.Simulation,
            "DB1.DBW0"));
        Assert.False(SiemensS7OptimizedBlockAccessAdvisor.ShouldRequireConfirmation(
            "modbus-tcp",
            DeviceMode.Hardware,
            "DB1.DBW0"));
    }

    private static DeviceConfig.DeviceEntry CreateDevice(DeviceMode mode)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "S7",
            Name = "西门子设备",
            ChannelId = Guid.NewGuid().ToString("D"),
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1200",
            DeviceMode = mode
        };

    private static PointsConfig.PointEntry CreatePoint(
        DeviceConfig.DeviceEntry device,
        string address)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "P1",
            Name = "压力",
            DeviceId = device.Id,
            DeviceCode = device.Code,
            Address = address,
            Protocol = "SiemensS7",
            DataType = "Float32",
            RawDataType = "Float32"
        };

    private static DeviceConfigurationSnapshot CreateSnapshot(
        DeviceConfig.DeviceEntry device,
        PointsConfig.PointEntry point)
        => new()
        {
            Revision = "test",
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                Devices = new List<DeviceConfig.DeviceEntry> { device }
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Points = new List<PointsConfig.PointEntry> { point }
            },
            Simulation = new SimulationConfig { SchemaVersion = SimulationConfig.CurrentSchemaVersion },
            SignalBindings = new SignalBindingsConfig()
        };
}
