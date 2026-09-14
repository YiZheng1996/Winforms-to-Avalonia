using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class ConfigurationValidatorTests
{
    private static (AppConfig, DeviceConfig, PointsConfig, SimulationConfig) Valid()
    {
        var app = new AppConfig { SchemaVersion = 1, SystemName = "s", Brand = "b", Language = "zh-CN", DefaultPaths = new AppPaths { Database = "d.db" } };
        var device = new DeviceConfig { SchemaVersion = 1, DeviceMode = DeviceMode.Simulation };
        device.Devices.Add(new DeviceConfig.DeviceEntry { Name = "Sim", Protocol = "Simulation", Address = "sim://1" });
        var points = new PointsConfig { SchemaVersion = 1 };
        points.Points.Add(new PointsConfig.PointEntry { Code = "AI_Pressure", Protocol = "Simulation", Address = "sim.pressure", DataType = "Decimal" });
        var sim = new SimulationConfig { SchemaVersion = 1 };
        sim.InitialValues["sim.pressure"] = 0.0;
        return (app, device, points, sim);
    }

    [Fact]
    public void ValidConfig_Passes()
    {
        var (app, device, points, sim) = Valid();
        ConfigurationValidator.ValidateAll(app, device, points, sim); // 不抛
    }

    [Fact]
    public void LegacyConfigWithNoDevicesDoesNotInferProjectMode()
    {
        var (app, device, points, sim) = Valid();
        device.DeviceMode = DeviceMode.Hardware;
        device.Devices.Clear();
        ConfigurationValidator.ValidateAll(app, device, points, sim);
    }

    [Fact]
    public void SimulationAddressNotInPoints_Throws()
    {
        var (app, device, points, sim) = Valid();
        sim.InitialValues["sim.missing"] = 1.0;
        Assert.Throws<ConfigValidationException>(() => ConfigurationValidator.ValidateAll(app, device, points, sim));
    }

    [Fact]
    public void ReadOnlyHighRiskPoint_Throws()
    {
        var (app, device, points, sim) = Valid();
        points.Points[0].RiskLevel = WriteRiskLevel.HighRisk;

        var exception = Assert.Throws<ConfigValidationException>(() => ConfigurationValidator.ValidateAll(app, device, points, sim));

        Assert.Contains("只读点位", exception.Message, StringComparison.Ordinal);
    }

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
    public void PointTypeCatalog_MapsKepServerStorageNames(
        string displayName,
        DevicePointDataType expected,
        string storageName)
    {
        Assert.True(DevicePointTypeCatalog.TryParseDataType(displayName, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(storageName, DevicePointTypeCatalog.ToStorage(actual));
        Assert.Equal(displayName, DevicePointTypeCatalog.ToDisplayName(actual));
    }

    [Fact]
    public void UnsupportedPointType_IsRejectedByConfigurationValidation()
    {
        var (app, device, points, sim) = Valid();
        points.Points[0].DataType = "UnsupportedType";

        var exception = Assert.Throws<ConfigValidationException>(() => ConfigurationValidator.ValidateAll(app, device, points, sim));

        Assert.Contains("数据类型不受支持", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedSimulationPattern_IsRejectedByConfigurationValidation()
    {
        var (app, device, points, sim) = Valid();
        sim.ChangeRules.Add(new SimulationConfig.ChangeRule
        {
            Address = "sim.pressure",
            Pattern = "triangle",
            Min = 0,
            Max = 10
        });

        var exception = Assert.Throws<ConfigValidationException>(() => ConfigurationValidator.ValidateAll(app, device, points, sim));

        Assert.Contains("变化方式不受支持", exception.Message, StringComparison.Ordinal);
    }
}
