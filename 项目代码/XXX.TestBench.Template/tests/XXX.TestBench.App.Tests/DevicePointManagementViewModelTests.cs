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
            var row = string.Join(',', "", "AI_New", "新压力", "SampleDevice", "", "仿真逻辑地址", "sim.new", "小数", "大端", "无", "MPa", "", "", "", "", "否", "是", "普通", "");
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
            Assert.Equal(DevicePointTemplateDefinition.InstructionsSheetName, workbook.Worksheet(2).Name);
            Assert.Equal(DevicePointTemplateDefinition.ExamplesSheetName, workbook.Worksheet(3).Name);
            Assert.Equal("试验压力", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 3).GetString());
            Assert.Equal("PRESSURE", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 5).GetString());
            Assert.Contains("模板已保存", vm.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task ValidateImport_DoesNotReplaceUntilApplyIsConfirmed()
    {
        using var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        var path = Path.Combine(Path.GetTempPath(), "testbench-point-preview-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            await vm.LoadAsync();
            var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
            var row = string.Join(',', "", "AI_Preview", "预览压力", "SampleDevice", "", "仿真逻辑地址", "sim.preview", "小数", "大端", "无", "MPa", "", "", "", "", "否", "是", "普通", "");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");

            var result = await vm.ValidateImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.Equal(2, vm.Points.Count);
            Assert.Contains("校验通过", vm.StatusMessage, StringComparison.Ordinal);

            await vm.ApplyImportedPointsAsync(result);

            Assert.Single(vm.Points);
            Assert.Equal("AI_Preview", vm.Points[0].Code);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
