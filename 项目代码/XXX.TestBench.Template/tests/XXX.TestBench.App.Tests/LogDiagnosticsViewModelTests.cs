using XXX.TestBench.App.ViewModels;
using Xunit;

namespace XXX.TestBench.App.Tests;

public sealed class LogDiagnosticsViewModelTests
{
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
}
