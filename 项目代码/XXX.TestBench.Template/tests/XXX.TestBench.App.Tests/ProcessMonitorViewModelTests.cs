using XXX.TestBench.App.ViewModels;
using XXX.TestBench.App.ViewModels.Process;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.Tests;

public sealed class ProcessMonitorViewModelTests
{
    [Fact]
    public async Task MissingProcessBindings_StayUnboundAndDoNotEnableOperations()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var viewModel = new ProcessMonitorViewModel(
            harness.Services,
            await harness.AdminActorAsync());

        await viewModel.LoadAsync();

        Assert.Equal(ProcessDataState.Unbound, viewModel.SafetyDoor.State);
        Assert.Equal("未绑定", viewModel.SafetyDoor.StatusText);
        Assert.Equal("--", viewModel.MainPressure.ValueText);
        Assert.Equal(ProcessDataState.Unbound, viewModel.PressureSetpoint.State);
        Assert.Equal("未配置可靠量程", viewModel.PressureSetpoint.RangeText);
        Assert.False(viewModel.InletValve.CanOperate);
        Assert.False(viewModel.PressureSetpoint.CanApply);
        Assert.Equal("--", viewModel.PressureSetpointReadback.ValueText);
    }
}
