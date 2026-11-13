using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Time;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public class SimulationRuntimeTests
{
    [Fact]
    public async Task Factory_Simulation_ReadsWritesAndMarksSimulation()
    {
        using var env = TestEnv.Create();
        var store = new JsonConfigStore(env.ConfigRoot);
        var deviceConfig = await store.LoadAsync<Core.Configuration.DeviceConfig>("device.json");
        var pointsConfig = await store.LoadAsync<Core.Configuration.PointsConfig>("points.json");
        var simConfig = await store.LoadAsync<Core.Configuration.SimulationConfig>("simulation.json");

        var factory = new DeviceRuntimeFactory(deviceConfig, pointsConfig, simConfig, new SystemClock());
        await using var runtime = await factory.CreateAsync(DeviceMode.Simulation);
        await runtime.StartAsync();

        Assert.True(runtime.IsSimulation);
        Assert.Equal(DeviceHealth.Healthy, runtime.Status.Health);

        var points = await runtime.ListPointsAsync();
        Assert.Equal(2, points.Count);

        var startPoint = points.First(p => p.Code == "DO_Start");
        var written = await runtime.WriteAsync(startPoint, true);
        Assert.Equal(PointQuality.Good, written.Quality);

        var read = await runtime.ReadAsync(startPoint);
        Assert.Equal(true, read.Value);
    }

    [Fact]
    public async Task Factory_Hardware_Fails_NoRuntime()
    {
        using var env = TestEnv.Create("Hardware");
        var store = new JsonConfigStore(env.ConfigRoot);
        var deviceConfig = await store.LoadAsync<Core.Configuration.DeviceConfig>("device.json");
        var pointsConfig = await store.LoadAsync<Core.Configuration.PointsConfig>("points.json");
        var simConfig = await store.LoadAsync<Core.Configuration.SimulationConfig>("simulation.json");

        var factory = new DeviceRuntimeFactory(deviceConfig, pointsConfig, simConfig, new SystemClock());
        await Assert.ThrowsAsync<Core.Common.DomainException>(() => factory.CreateAsync(DeviceMode.Hardware));
    }
}
