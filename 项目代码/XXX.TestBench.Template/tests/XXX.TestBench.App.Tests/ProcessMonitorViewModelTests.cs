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

    [Fact]
    public async Task DemoData_OnlyChangesDisplayProjection()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var viewModel = new ProcessMonitorViewModel(
            harness.Services,
            await harness.AdminActorAsync());

        await viewModel.LoadAsync();
        viewModel.ToggleDemoDataCommand.Execute(null);

        Assert.True(viewModel.IsDemoData);
        Assert.Equal("0.800", viewModel.SupplyPressure.DisplayValueText);
        Assert.Equal("0.628", viewModel.MainPressure.DisplayValueText);
        Assert.Equal("0.625", viewModel.DutPressure.DisplayValueText);
        Assert.Equal("已闭合", viewModel.SafetyDoor.DisplayActiveText);
        Assert.Equal("已到位", viewModel.ClampReady.DisplayActiveText);
        Assert.Equal("已开启", viewModel.InletValve.DisplayFeedbackText);
        Assert.Equal("已关闭", viewModel.ExhaustValve.DisplayFeedbackText);
        Assert.Equal("0.650", viewModel.PressureSetpoint.TargetText);
        Assert.Equal("0.628", viewModel.PressureSetpoint.ReadbackText);
        Assert.Equal(ProcessDataState.Unbound, viewModel.SafetyDoor.State);
        Assert.False(viewModel.InletValve.CanOperate);
        Assert.False(viewModel.InletValve.OpenCommand!.CanExecute(null));
        Assert.True(viewModel.InletValve.DisplayOpenCommand!.CanExecute(null));
        Assert.False(viewModel.PressureSetpoint.ApplyCommand!.CanExecute(null));
        Assert.True(viewModel.PressureSetpoint.DisplayApplyCommand!.CanExecute(null));
    }
}
