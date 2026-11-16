using ClosedXML.Excel;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices;
using XXX.TestBench.Infrastructure.Configuration;
using Xunit;

namespace XXX.TestBench.App.Tests;

public sealed class XinjeDownloadedTemplateWorkflowTests
{
    private static DevicePointTreeNodeViewModel? FindNode(
        IEnumerable<DevicePointTreeNodeViewModel> nodes,
        Func<DevicePointTreeNodeViewModel, bool> predicate)
    {
        foreach (var node in nodes)
        {
            if (predicate(node))
                return node;
            var child = FindNode(node.Children, predicate);
            if (child is not null)
                return child;
        }
        return null;
    }
    [Fact]
    public async Task DownloadedXinjeTemplate_CanPreviewApplyAndReimportWithoutChanges()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var active = await harness.Services.DeviceConfigurations!.LoadActiveAsync();
        var channel = active.Device.Channels.Single(item => item.Code == "CH_EMPTY");
        var xinje = CreateXinjeDevice(channel.Id);
        var candidateDevice = new DeviceConfig
        {
            SchemaVersion = active.Device.SchemaVersion,
            Channels = active.Device.Channels.ToList(),
            Devices = active.Device.Devices.Append(xinje).ToList()
        };
        var deviceApplied = await harness.Services.DeviceConfigurations.ApplyDeviceConfigurationAsync(
            actor, candidateDevice, new DeviceConfigurationApplyOptions());
        Assert.True(deviceApplied.Ok, deviceApplied.Error);

        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        vm.SelectedTreeNode = FindNode(vm.TreeNodes, node =>
            node.Kind == DevicePointTreeNodeKind.Device && node.Code == xinje.Code);
        Assert.NotNull(vm.SelectedTreeNode);

        var path = NewTemplatePath();
        try
        {
            await vm.DownloadTemplateAsync(path);
            using (var workbook = new XLWorkbook(path))
            {
                var dataSheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
                Assert.Equal(9, dataSheet.LastColumnUsed()!.ColumnNumber());
                Assert.All(
                    dataSheet.Range(2, 1, 2, DevicePointTemplateDefinition.Columns.Count).Cells(),
                    cell => Assert.True(string.IsNullOrWhiteSpace(cell.GetString())));
                Assert.Equal("HD1000", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 2).GetString());
                Assert.Equal("浮点型", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 3).GetString());
                Assert.Equal("M400", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(3, 2).GetString());
                Assert.Equal("布尔量", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(3, 3).GetString());
                Assert.Equal("读写", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(3, 4).GetString());
                Assert.All(Enumerable.Range(5, 4), column =>
                    Assert.True(string.IsNullOrWhiteSpace(
                        workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(3, column).GetString())));
                Assert.Contains(
                    dataSheet.DataValidations.Select(validation => validation.Value),
                    value => value.Contains("字,长整型,双字,浮点型,布尔量", StringComparison.Ordinal));
                var instructionText = string.Join("\n",
                    workbook.Worksheet(DevicePointTemplateDefinition.InstructionsSheetName)
                        .RangeUsed()!.Cells().Select(cell => cell.GetString()));
                Assert.Contains("D100", instructionText, StringComparison.Ordinal);
                Assert.Contains("HD1000", instructionText, StringComparison.Ordinal);
                Assert.Contains("M400", instructionText, StringComparison.Ordinal);
                Assert.Contains("HM40", instructionText, StringComparison.Ordinal);
                Assert.Contains("大端模式、低字在前", instructionText, StringComparison.Ordinal);

                var exampleSheet = workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName);
                for (var columnIndex = 1; columnIndex <= DevicePointTemplateDefinition.Columns.Count; columnIndex++)
                    dataSheet.Cell(2, columnIndex).Value = exampleSheet.Cell(2, columnIndex).Value;
                for (var columnIndex = 1; columnIndex <= DevicePointTemplateDefinition.Columns.Count; columnIndex++)
                    dataSheet.Cell(3, columnIndex).Value = exampleSheet.Cell(3, columnIndex).Value;
                workbook.Save();
            }

            var beforePreview = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            var samplePlan = await vm.CreateImportPlanAsync(path);
            Assert.NotNull(samplePlan);
            Assert.True(samplePlan!.CanApply, string.Join("；", samplePlan.Rows.SelectMany(row => row.Issues)));
            var afterPreview = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            Assert.Equal(beforePreview.Revision, afterPreview.Revision);
            Assert.Equal(beforePreview.Points.Points.Count, afterPreview.Points.Points.Count);

            using (var workbook = new XLWorkbook(path))
            {
                var dataSheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
                dataSheet.Range(2, 1, 5, DevicePointTemplateDefinition.Columns.Count).Clear(XLClearOptions.Contents);
                SetPointRow(dataSheet, 2, "D_WORD", "D100", "字", "只读", "D 区字示例");
                SetPointRow(dataSheet, 3, "HD_FLOAT", "HD1000", "浮点型", "只读", "HD 区浮点示例");
                SetPointRow(dataSheet, 4, "M_BOOL", "M400", "布尔量", "只读", "M 区布尔示例");
                SetPointRow(dataSheet, 5, "HM_BOOL", "HM40", "布尔量", "只读", "HM 区布尔示例");
                workbook.Save();
            }

            var plan = await vm.CreateImportPlanAsync(path);
            Assert.NotNull(plan);
            Assert.True(plan!.CanApply, string.Join("；", plan.Rows.SelectMany(row => row.Issues)));
            Assert.Equal(4, plan.Rows.Count);

            var applied = await vm.ApplyImportedPointsAsync(plan, new DeviceConfigurationApplyOptions());
            Assert.True(applied.Succeeded, applied.Message);
            var persisted = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            AssertPointAddress(persisted.Points.Points.Single(point => point.Code == "D_WORD"), "D100", "HR", 100);
            AssertPointAddress(persisted.Points.Points.Single(point => point.Code == "HD_FLOAT"), "HD1000", "HR", 42088);
            AssertPointAddress(persisted.Points.Points.Single(point => point.Code == "M_BOOL"), "M400", "C", 400);
            AssertPointAddress(persisted.Points.Points.Single(point => point.Code == "HM_BOOL"), "HM40", "C", 49448);

            var unchangedPlan = await vm.CreateImportPlanAsync(path);
            Assert.NotNull(unchangedPlan);
            Assert.All(unchangedPlan!.Rows, row => Assert.Equal(DevicePointImportOperationKind.Unchanged, row.Operation));
            var beforeNoOpApply = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            var noOpResult = await vm.ApplyImportedPointsAsync(unchangedPlan, new DeviceConfigurationApplyOptions());
            Assert.True(noOpResult.Succeeded, noOpResult.Message);
            var afterNoOpApply = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            Assert.Equal(beforeNoOpApply.Revision, afterNoOpApply.Revision);
            Assert.Equal(beforeNoOpApply.Points.Points.Count, afterNoOpApply.Points.Points.Count);
        }
        finally
        {
            DeleteTemplatePath(path);
        }
    }

    [Fact]
    public async Task DownloadedXinjeTemplate_StillRejectsUnsupportedAddressOrType()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var active = await harness.Services.DeviceConfigurations!.LoadActiveAsync();
        var channel = active.Device.Channels.Single(item => item.Code == "CH_EMPTY");
        var xinje = CreateXinjeDevice(channel.Id);
        var candidateDevice = new DeviceConfig
        {
            SchemaVersion = active.Device.SchemaVersion,
            Channels = active.Device.Channels.ToList(),
            Devices = active.Device.Devices.Append(xinje).ToList()
        };
        var deviceApplied = await harness.Services.DeviceConfigurations.ApplyDeviceConfigurationAsync(
            actor, candidateDevice, new DeviceConfigurationApplyOptions());
        Assert.True(deviceApplied.Ok, deviceApplied.Error);

        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        vm.SelectedTreeNode = FindNode(vm.TreeNodes, node =>
            node.Kind == DevicePointTreeNodeKind.Device && node.Code == xinje.Code);
        Assert.NotNull(vm.SelectedTreeNode);

        var path = NewTemplatePath();
        try
        {
            await vm.DownloadTemplateAsync(path);
            using (var workbook = new XLWorkbook(path))
            {
                var dataSheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
                SetPointRow(dataSheet, 2, "BAD_SHORT", "D100", "短整型", "只读", "不支持的信捷类型");
                SetPointRow(dataSheet, 3, "BAD_DOUBLE", "HD1002", "双精度", "只读", "不支持的信捷类型");
                SetPointRow(dataSheet, 4, "BAD_M_TYPE", "M400", "浮点型", "只读", "M 区不支持浮点型");
                SetPointRow(dataSheet, 5, "BAD_HR", "HR:0", "浮点型", "只读", "通用地址不适用于信捷型号");
                SetPointRow(dataSheet, 6, "BAD_C", "C:1", "布尔量", "只读", "通用地址不适用于信捷型号");
                SetPointRow(dataSheet, 7, "BAD_BIT", "D10.0", "字", "只读", "不支持寄存器位地址");
                SetPointRow(dataSheet, 8, "BAD_AREA", "FD100", "浮点型", "只读", "不支持该信捷地址区");
                workbook.Save();
            }

            var beforePreview = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            var plan = await vm.CreateImportPlanAsync(path);
            Assert.Null(plan);
            Assert.Contains("导入未保存", vm.StatusMessage, StringComparison.Ordinal);
            var issueDetails = vm.StatusMessage;
            Assert.True(issueDetails.Split('；').Length >= 7, issueDetails);
            foreach (var rejectedValue in new[] { "BAD_SHORT", "BAD_DOUBLE", "BAD_M_TYPE", "HR:0", "C:1", "D10.0", "FD100" })
                Assert.Contains(rejectedValue, issueDetails, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(issueDetails));
            var afterPreview = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            Assert.Equal(beforePreview.Revision, afterPreview.Revision);
            Assert.Equal(beforePreview.Points.Points.Count, afterPreview.Points.Points.Count);
        }
        finally
        {
            DeleteTemplatePath(path);
        }
    }

    private static DeviceConfig.DeviceEntry CreateXinjeDevice(string channelId)
        => new()
        {
            Id = "20000000-0000-0000-0000-000000000099",
            Code = "DEV_XINJE",
            Name = "信捷 PLC",
            ChannelId = channelId,
            DriverKey = DriverKeyCatalog.ModbusTcp,
            Model = DeviceModelKeyCatalog.XinjeFieldV1,
            DeviceMode = DeviceMode.Simulation,
            ModbusUnitId = 1,
            ModbusDataOrder = new ModbusDataOrderOptions
            {
                ByteOrder = ByteOrder.BigEndian,
                WordOrder = WordOrder.LowWordFirst
            },
            ModbusTcp = new ModbusTcpConnectionOptions { Host = "127.0.0.1", Port = 502 },
            PollIntervalMs = 500,
            StaleAfterMs = 1500
        };
    private static void SetPointRow(
        IXLWorksheet worksheet,
        int row,
        string pointTag,
        string address,
        string dataType,
        string access,
        string description)
    {
        worksheet.Cell(row, 1).Value = pointTag;
        worksheet.Cell(row, 2).Value = address;
        worksheet.Cell(row, 3).Value = dataType;
        worksheet.Cell(row, 4).Value = access;
        worksheet.Cell(row, 9).Value = description;
    }

    private static void AssertPointAddress(
        PointsConfig.PointEntry point,
        string nativeAddress,
        string area,
        int offset)
    {
        Assert.Equal(nativeAddress, point.Address);
        Assert.Equal(area, point.AddressDefinition?.Area);
        Assert.Equal(offset, point.AddressDefinition?.Offset);
    }

    private static string NewTemplatePath()
    {
        var directory = Path.Combine(
            @"D:\Codex相关\信捷PLC_XLSX导入模板修正\test-files",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "downloaded-template.xlsx");
    }

    private static void DeleteTemplatePath(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        var directory = Path.GetFullPath(Path.GetDirectoryName(path)!);
        var taskRoot = Path.GetFullPath(@"D:\Codex相关\信捷PLC_XLSX导入模板修正\test-files")
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(taskRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("测试模板路径超出本用例目录");
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }

}
