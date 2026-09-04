using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Configuration;
using ClosedXML.Excel;

namespace XXX.TestBench.App.Tests;

public sealed class DevicePointManagementViewModelTests
{
    [Fact]
    public async Task Import_ReplacesCatalogAndReloadsSimulationRuntime()
    {
        using var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        var path = Path.Combine(Path.GetTempPath(), "testbench-point-vm-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            await vm.LoadAsync();
            Assert.Equal(2, vm.Points.Count);

            var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
            var row = string.Join(',', "AI_New", "新压力", "Simulation", "sim.new", "Decimal", "MPa", "", "", "", "", "否", "普通", "是", "");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");
            await vm.ImportAsync(path);

            var point = Assert.Single(vm.Points);
            Assert.Equal("AI_New", point.Code);
            Assert.Contains("已导入", vm.StatusMessage, StringComparison.Ordinal);
            var runtimePoints = await harness.Services.DeviceModes.Runtime!.ListPointsAsync();
            Assert.Equal("AI_New", Assert.Single(runtimePoints).Code);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task DownloadTemplate_ProducesFixedChineseHeaderWorkbook()
    {
        using var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        var path = Path.Combine(Path.GetTempPath(), "testbench-point-template-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            await vm.DownloadTemplateAsync(path);

            Assert.True(File.Exists(path));
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            Assert.Equal(DevicePointTemplateDefinition.Columns.Count, sheet.LastColumnUsed()!.ColumnNumber());
            for (var index = 0; index < DevicePointTemplateDefinition.Columns.Count; index++)
                Assert.Equal(DevicePointTemplateDefinition.Columns[index].Header, sheet.Cell(1, index + 1).GetString());
            Assert.Contains("模板已保存", vm.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
