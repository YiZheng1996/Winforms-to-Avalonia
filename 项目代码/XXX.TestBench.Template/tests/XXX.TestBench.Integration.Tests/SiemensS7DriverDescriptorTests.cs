using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices.Drivers;

namespace XXX.TestBench.Integration.Tests;

public sealed class SiemensS7DriverDescriptorTests
{
    private readonly SiemensS7DriverDescriptor _descriptor = new();

    [Fact]
    public void DeviceModels_AreFixedCustomerSelectionsWithAddressHelp()
    {
        Assert.Equal(
            new[] { "S7-200 SMART", "S7-1200", "S7-1500" },
            _descriptor.DeviceModels.Select(model => model.DisplayName));
        Assert.Contains("VW5022", _descriptor.DeviceModels[0].AddressHint, StringComparison.Ordinal);
        Assert.Contains("DB144.DBD88", _descriptor.DeviceModels[1].AddressHint, StringComparison.Ordinal);
        Assert.Contains("DB144.DBD88", _descriptor.DeviceModels[2].AddressHint, StringComparison.Ordinal);
    }

    [Fact]
    public void S71500_AcceptsDbDoubleWordAddress()
    {
        var issues = _descriptor.ValidatePoint(
            Point("DB144.DBD88", DevicePointDataType.Float32),
            Device("S7-1500"));

        Assert.Empty(issues);
    }

    [Fact]
    public void S71500_RejectsS7200StyleAddress()
    {
        var issues = _descriptor.ValidatePoint(
            Point("VW5022", DevicePointDataType.UInt16),
            Device("S7-1500"));

        Assert.Contains(issues, issue => issue.Message.Contains("S7-1500", StringComparison.Ordinal));
    }

    [Fact]
    public void S7200_AcceptsVWordAddress()
    {
        var issues = _descriptor.ValidatePoint(
            Point("VW5022", DevicePointDataType.UInt16),
            Device("S7-200 SMART"));

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("DB142.DBX22.3", DevicePointDataType.Boolean, "S7-1500")]
    [InlineData("DB144.DBD88", DevicePointDataType.Float32, "S7-1200")]
    [InlineData("VD800", DevicePointDataType.UInt32, "S7-200 SMART")]
    [InlineData("V0.1", DevicePointDataType.Boolean, "S7-200 SMART")]
    [InlineData("M10.1", DevicePointDataType.Boolean, "S7-200 SMART")]
    public void AcceptsAddressShapesUsedByKepServerReference(
        string address,
        DevicePointDataType type,
        string profile)
    {
        var issues = _descriptor.ValidatePoint(Point(address, type), Device(profile));

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("DB140.DBB2", "S7-1500")]
    [InlineData("VB800", "S7-200 SMART")]
    public void ByteAddress_AcceptsByteType(string address, string profile)
    {
        var issues = _descriptor.ValidatePoint(
            Point(address, DevicePointDataType.Byte),
            Device(profile));

        Assert.Empty(issues);
    }

    [Fact]
    public void ByteAddress_RejectsWiderNumericType()
    {
        var issues = _descriptor.ValidatePoint(
            Point("DB140.DBB2", DevicePointDataType.Float32),
            Device("S7-1500"));

        Assert.Contains(issues, issue => issue.Path == "rawDataType");
    }

    [Fact]
    public void BitAddressRequiresBooleanType()
    {
        var issues = _descriptor.ValidatePoint(
            Point("DB142.DBX22.3", DevicePointDataType.Float32),
            Device("S7-1500"));

        Assert.Contains(issues, issue => issue.Path == "rawDataType");
    }

    [Fact]
    public void ImportPreflightRejectsAddressBeforeConfigurationApply()
    {
        var deviceId = Guid.NewGuid().ToString();
        var config = new DeviceConfig
        {
            Devices =
            [
                new DeviceConfig.DeviceEntry
                {
                    Id = deviceId,
                    Code = "S71500",
                    Name = "S7 控制器",
                    DriverKey = DriverKeyCatalog.SiemensS7,
                    Model = "S7-1500"
                }
            ]
        };
        var point = Point("VW5022", DevicePointDataType.UInt16);
        point.DeviceId = deviceId;
        point.DeviceCode = "S71500";

        var issues = DevicePointImportValidator.Validate(
            config,
            new[] { point },
            new[] { _descriptor });

        Assert.Contains(issues, issue => issue.Message.Contains("无法导入该设备", StringComparison.Ordinal));
    }

    private static PointsConfig.PointEntry Point(string address, DevicePointDataType type)
        => new()
        {
            Code = "AI.Pressure",
            Address = address,
            RawDataType = DevicePointTypeCatalog.ToStorage(type),
            DataType = DevicePointTypeCatalog.ToStorage(type)
        };

    private static DeviceConfig.DeviceEntry Device(string profile)
        => new()
        {
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = profile
        };
}
