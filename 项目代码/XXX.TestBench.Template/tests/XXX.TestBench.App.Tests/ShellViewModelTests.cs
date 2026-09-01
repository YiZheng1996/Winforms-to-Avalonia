using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Devices;
using Xunit;

namespace XXX.TestBench.App.Tests;

public class ShellViewModelTests
{
    [Fact]
    public void Simulation_ShowsSimulationBadge_AndNormalStatus()
    {
        var vm = new ShellViewModel("测试系统", "0.1.0", DeviceMode.Simulation, "C:\\data\\testbench.db");

        Assert.Equal("Simulation（仿真）", vm.DeviceModeText);
        Assert.True(vm.IsSimulationMode);
        Assert.Equal("运行正常", vm.ConnectionStatusText);
        Assert.False(vm.IsFaulted);
    }

    [Fact]
    public void Hardware_ShowsHardwareBadge()
    {
        var vm = new ShellViewModel("测试系统", "0.1.0", DeviceMode.Hardware, "C:\\data\\testbench.db");

        Assert.Equal("Hardware（硬件）", vm.DeviceModeText);
        Assert.False(vm.IsSimulationMode);
    }

    [Fact]
    public void Faulted_ShowsDiagnostic_AndFaultStatus()
    {
        var vm = new ShellViewModel("测试系统", "0.1.0", DeviceMode.Simulation, "data", isFaulted: true, faultMessage: "device.json schemaVersion=99 不受支持");

        Assert.True(vm.IsFaulted);
        Assert.Equal("故障", vm.ConnectionStatusText);
        Assert.Contains("schemaVersion", vm.FaultMessage);
        Assert.Equal("未登录", vm.CurrentUserText);
    }

    [Fact]
    public void DeviceError_ShowsInStatus()
    {
        var vm = new ShellViewModel("测试系统", "0.1.0", DeviceMode.Hardware, "data", deviceError: "Hardware 无适配器");

        Assert.Equal("Hardware 无适配器", vm.ConnectionStatusText);
    }
}
