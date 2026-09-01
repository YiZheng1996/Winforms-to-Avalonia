using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Devices;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Logging;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Time;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public class DeviceModeControllerIntegrationTests
{
    [Fact]
    public async Task HardwareInitFailure_KeepsHardwareMode_Faulted_NoFallback()
    {
        using var env = TestEnv.Create("Hardware");
        var store = new JsonConfigStore(env.ConfigRoot);
        var deviceConfig = await store.LoadAsync<Core.Configuration.DeviceConfig>("device.json");
        var pointsConfig = await store.LoadAsync<Core.Configuration.PointsConfig>("points.json");
        var simConfig = await store.LoadAsync<Core.Configuration.SimulationConfig>("simulation.json");

        var dbFactory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(dbFactory, new Pbkdf2PasswordHasher()).InitializeAsync();
        var factory = new DeviceRuntimeFactory(deviceConfig, pointsConfig, simConfig, new SystemClock());
        var audit = new SqliteAuditLog(dbFactory);
        var controller = new DeviceModeController(factory, new FileLogger(Path.Combine(env.DataRoot, "logs")), audit);

        var result = await controller.InitializeAsync(DeviceMode.Hardware);

        Assert.False(result.Ok);
        Assert.Equal(DeviceMode.Hardware, controller.CurrentMode);
        Assert.Equal(DeviceHealth.Faulted, controller.Health);
        Assert.Null(controller.Runtime);
    }

    [Fact]
    public async Task SimulationInit_Succeeds_AndSwitchRequiresPermission()
    {
        using var env = TestEnv.Create();
        var store = new JsonConfigStore(env.ConfigRoot);
        var deviceConfig = await store.LoadAsync<Core.Configuration.DeviceConfig>("device.json");
        var pointsConfig = await store.LoadAsync<Core.Configuration.PointsConfig>("points.json");
        var simConfig = await store.LoadAsync<Core.Configuration.SimulationConfig>("simulation.json");

        var dbFactory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(dbFactory, new Pbkdf2PasswordHasher()).InitializeAsync();
        var factory = new DeviceRuntimeFactory(deviceConfig, pointsConfig, simConfig, new SystemClock());
        var audit = new SqliteAuditLog(dbFactory);
        var controller = new DeviceModeController(factory, new FileLogger(Path.Combine(env.DataRoot, "logs")), audit);

        var result = await controller.InitializeAsync(DeviceMode.Simulation);
        Assert.True(result.Ok);
        Assert.Equal(DeviceHealth.Healthy, controller.Health);
        Assert.True(controller.Runtime!.IsSimulation);
    }
}
