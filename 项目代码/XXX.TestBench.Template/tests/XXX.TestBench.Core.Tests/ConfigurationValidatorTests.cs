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
    public void HardwareWithoutDevice_Throws()
    {
        var (app, device, points, sim) = Valid();
        device.DeviceMode = DeviceMode.Hardware;
        device.Devices.Clear();
        Assert.Throws<ConfigValidationException>(() => ConfigurationValidator.ValidateAll(app, device, points, sim));
    }

    [Fact]
    public void SimulationAddressNotInPoints_Throws()
    {
        var (app, device, points, sim) = Valid();
        sim.InitialValues["sim.missing"] = 1.0;
        Assert.Throws<ConfigValidationException>(() => ConfigurationValidator.ValidateAll(app, device, points, sim));
    }
}
