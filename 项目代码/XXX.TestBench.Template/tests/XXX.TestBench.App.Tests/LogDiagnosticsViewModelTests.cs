using XXX.TestBench.App.ViewModels;
using Xunit;

namespace XXX.TestBench.App.Tests;

public sealed class LogDiagnosticsViewModelTests
{
    [Fact]
    public async Task NewViewModel_UsesRecentSevenDaysAndResetRestoresDefaults()
    {
        using var harness = AppTestHarness.Create();
        var viewModel = new LogDiagnosticsViewModel(harness.Services, await harness.AdminActorAsync());

        Assert.Equal(DateTime.Today.AddDays(-6), viewModel.StartDate!.Value.Date);
        Assert.Equal(DateTime.Today, viewModel.EndDate!.Value.Date);
        Assert.Equal("全部操作", viewModel.SelectedAction!.DisplayName);

        viewModel.ActorFilter = "admin";
        viewModel.TextFilter = "record";
        viewModel.StartDate = null;
        viewModel.EndDate = null;
        viewModel.SelectedAction = null;

        await viewModel.ResetFiltersAsync();

        Assert.Empty(viewModel.ActorFilter);
        Assert.Empty(viewModel.TextFilter);
        Assert.Equal(DateTime.Today.AddDays(-6), viewModel.StartDate!.Value.Date);
        Assert.Equal(DateTime.Today, viewModel.EndDate!.Value.Date);
        Assert.Equal("全部操作", viewModel.SelectedAction!.DisplayName);
    }

    [Fact]
    public async Task LoadAsync_ConvertsAuditCodesAndValuesToChineseDisplayText()
    {
        using var harness = AppTestHarness.Create();
        await harness.Services.AuditLog.WriteAsync("system", "DeviceModeInitialized", "Simulation", null);
        await harness.Services.AuditLog.WriteAsync("admin", "TestStarted", "record:12", "model:7 R-20260904-0001");
        await harness.Services.AuditLog.WriteAsync("admin", "ItemExecuted", "record:12", "point:3 Passed");
        await harness.Services.AuditLog.WriteAsync("admin", "DeviceWrite", "DO_Start", "value=False readback=False");
        await harness.Services.AuditLog.WriteAsync("admin", "AccessDenied", "ViewLogs", null);

        var viewModel = new LogDiagnosticsViewModel(harness.Services, await harness.AdminActorAsync());
        await viewModel.LoadAsync();

        Assert.Contains(viewModel.Logs, row => row.Actor == "系统" && row.Action == "设备模式初始化" && row.Target == "仿真模式" && row.Detail == "无");
        Assert.Contains(viewModel.Logs, row => row.Action == "开始试验"
            && row.Target == "试验记录（编号：12）"
            && row.Detail == "产品型号编号：7；记录编号：R-20260904-0001");
        Assert.Contains(viewModel.Logs, row => row.Action == "执行试验项点"
            && row.Detail == "试验项点编号：3；结果：合格");
        Assert.Contains(viewModel.Logs, row => row.Action == "设备点位写入"
            && row.Target == "设备点位：DO_Start"
            && row.Detail == "写入值：否；回读值：否");
        Assert.Contains(viewModel.Logs, row => row.Action == "权限不足"
            && row.Target == "权限：查看日志"
            && row.Detail == "无");

        Assert.DoesNotContain(viewModel.Logs, row => row.Action.Contains("Succeeded", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Logs, row => row.Action.Contains("Executed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_AppliesActorActionTextAndDateFilters()
    {
        using var harness = AppTestHarness.Create();
        await harness.Services.AuditLog.WriteAsync("admin", "TestStarted", "record:12", "model:7 R-20260904-0001");
        await harness.Services.AuditLog.WriteAsync("operator", "TestStarted", "record:13", "model:7 R-20260904-0002");

        var viewModel = new LogDiagnosticsViewModel(harness.Services, await harness.AdminActorAsync())
        {
            ActorFilter = "admin",
            SelectedAction = null,
            TextFilter = "R-20260904-0001",
            StartDate = DateTime.Today,
            EndDate = DateTime.Today
        };
        viewModel.SelectedAction = viewModel.ActionOptions.Single(option => option.Code == "TestStarted");

        await viewModel.SearchAsync();

        var row = Assert.Single(viewModel.Logs);
        Assert.Equal("开始试验", row.Action);
        Assert.Equal("试验记录（编号：12）", row.Target);

        viewModel.ActorFilter = string.Empty;
        viewModel.SelectedAction = viewModel.ActionOptions[0];
        viewModel.TextFilter = string.Empty;
        viewModel.StartDate = DateTime.Today.AddDays(1);
        viewModel.EndDate = DateTime.Today.AddDays(1);
        await viewModel.SearchAsync();
        Assert.Empty(viewModel.Logs);

        viewModel.StartDate = DateTime.Today.AddDays(2);
        viewModel.EndDate = DateTime.Today.AddDays(1);
        await viewModel.SearchAsync();
        Assert.Equal("结束日期不能早于开始日期", viewModel.StatusMessage);
        Assert.Empty(viewModel.Logs);
    }
}
