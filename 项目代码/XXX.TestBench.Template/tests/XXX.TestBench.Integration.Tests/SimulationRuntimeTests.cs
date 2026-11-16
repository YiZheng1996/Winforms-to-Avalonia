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
        var factory = new DeviceRuntimeFactory(
            await env.LoadSnapshotAsync(), new SystemClock(),
            requiredSignals: TestEnv.RegisteredRequiredSignals());
        await using var runtime = await factory.CreateAsync();
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
        var factory = new DeviceRuntimeFactory(
            await env.LoadSnapshotAsync(), new SystemClock(),
            requiredSignals: TestEnv.RegisteredRequiredSignals());
        await using var runtime = await factory.CreateAsync();
        await runtime.StartAsync();

        Assert.Equal(DeviceMode.Hardware, runtime.Mode);
        Assert.False(runtime.IsSimulation);
        Assert.False(runtime.ActivationReport.AllOnline);
        Assert.NotEmpty(runtime.ActivationReport.Issues);
        Assert.Equal(DeviceHealth.Faulted, runtime.Status.Health);
    }
}
