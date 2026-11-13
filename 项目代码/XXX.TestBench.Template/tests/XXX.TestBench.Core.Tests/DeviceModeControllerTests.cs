using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class DeviceModeControllerTests
{
    private static (DeviceModeController Controller, FakeRuntimeFactory Factory) Create()
    {
        var logger = new SilentLogger();
        var audit = new FakeAuditLog();
        var factory = new FakeRuntimeFactory();
        return (new DeviceModeController(factory, logger, audit), factory);
    }

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    [Fact]
    public async Task InitializeSimulation_Succeeds()
    {
        var (controller, _) = Create();
        var result = await controller.InitializeAsync();

        Assert.True(result.Ok);
        Assert.Equal(DeviceMode.Simulation, controller.CurrentMode);
        Assert.Equal(DeviceHealth.Healthy, controller.Health);
        Assert.False(controller.Runtime!.IsSimulation == false);
    }

    [Fact]
    public async Task FactoryFailure_EntersFaulted_WithoutSimulationFallback()
    {
        var controller = new DeviceModeController(new FailedRuntimeFactory(), new SilentLogger(), new FakeAuditLog());
        var result = await controller.InitializeAsync();

        Assert.False(result.Ok);
        Assert.Equal(DeviceHealth.Faulted, controller.Health);
        Assert.Null(controller.Runtime);
    }

    private sealed class FailedRuntimeFactory : IDeviceRuntimeFactory
    {
        public Task<IDeviceRuntime> CreateAsync(CancellationToken ct = default)
            => throw new DomainException("设备快照拒绝创建运行时");
    }
}
