using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public sealed class DevicePointTypeCatalogTests
{
    [Fact]
    public void PointTypeCatalog_MapsChineseTemplateValuesToEnums()
    {
        Assert.True(DevicePointTypeCatalog.TryParseProtocol("仿真", out var protocol));
        Assert.Equal(DevicePointProtocol.Simulation, protocol);
        Assert.True(DevicePointTypeCatalog.TryParseDataType("小数", out var dataType));
        Assert.Equal(DevicePointDataType.Decimal, dataType);
        Assert.Equal("Simulation", DevicePointTypeCatalog.ToStorage(protocol));
        Assert.Equal("Decimal", DevicePointTypeCatalog.ToStorage(dataType));
    }

    [Theory]
    [InlineData("字符", DevicePointDataType.Char, "Char")]
    [InlineData("字节", DevicePointDataType.Byte, "Byte")]
    [InlineData("短整型", DevicePointDataType.Int16, "Int16")]
    [InlineData("字", DevicePointDataType.UInt16, "UInt16")]
    [InlineData("长整型", DevicePointDataType.Int32, "Int32")]
    [InlineData("双字", DevicePointDataType.UInt32, "UInt32")]
    [InlineData("浮点型", DevicePointDataType.Float32, "Float32")]
    [InlineData("双精度", DevicePointDataType.Double, "Double")]
    [InlineData("Boolean", DevicePointDataType.Bool, "Bool")]
    [InlineData("开关量", DevicePointDataType.Bool, "Bool")]
    public void PointTypeCatalog_MapsPublishedStorageNames(
        string displayName,
        DevicePointDataType expected,
        string storageName)
    {
        Assert.True(DevicePointTypeCatalog.TryParseDataType(displayName, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(storageName, DevicePointTypeCatalog.ToStorage(actual));
    }

    [Fact]
    public void DataTypeEnum_LeavesRemovedBooleanSlotUnused()
    {
        Assert.Equal(5, (int)DevicePointDataType.Bool);
        Assert.DoesNotContain(Enum.GetValues<DevicePointDataType>(), value => (int)value == 1);
    }
}
