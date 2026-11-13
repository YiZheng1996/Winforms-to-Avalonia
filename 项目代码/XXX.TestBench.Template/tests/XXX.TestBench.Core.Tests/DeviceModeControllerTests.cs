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
        var result = await controller.InitializeAsync(DeviceMode.Simulation);

        Assert.True(result.Ok);
        Assert.Equal(DeviceMode.Simulation, controller.CurrentMode);
        Assert.Equal(DeviceHealth.Healthy, controller.Health);
        Assert.False(controller.Runtime!.IsSimulation == false);
    }

    [Fact]
    public async Task HardwareInitFailure_KeepsHardwareMode_AndEntersFaulted_NoFallback()
    {
        var (controller, _) = Create();
        var result = await controller.InitializeAsync(DeviceMode.Hardware);

        Assert.False(result.Ok);
        Assert.Equal(DeviceMode.Hardware, controller.CurrentMode); // 保留 Hardware 模式
        Assert.Equal(DeviceHealth.Faulted, controller.Health);
        Assert.Null(controller.Runtime); // 不得创建 Simulation 运行时
    }

    [Fact]
    public async Task SwitchMode_RequiresManageDevices_AndNoActiveTask()
    {
        var (controller, _) = Create();
        var operatorActor = TestContexts.With(PermissionCode.ExecuteTests);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            controller.SwitchModeAsync(operatorActor, DeviceMode.Hardware, hasActiveTask: false));

        var maintenance = TestContexts.With(PermissionCode.ManageDevices);
        var blocked = await controller.SwitchModeAsync(maintenance, DeviceMode.Hardware, hasActiveTask: true);
        Assert.False(blocked.Ok);
        Assert.Contains("活动试验", blocked.Error);
    }
}
