using ClosedXML.Excel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Integration.Tests;

public sealed class DevicePointImporterTests
{
    [Fact]
    public async Task CsvImport_SupportsChineseHeadersAndScale()
    {
        var path = TempFile(".csv");
        try
        {
            await File.WriteAllTextAsync(path, "点位标签,地址,数据类型,访问权限,原始下限,原始上限,工程下限,工程上限,说明\nAI.温度,sim.temp,小数,只读,0,10000,-40,120,温度输入");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            var point = Assert.Single(result.Points);
            Assert.Equal("AI.温度", point.Code);
            Assert.Equal("温度", point.Name);
            Assert.Equal("Simulation", point.Protocol);
            Assert.Equal("Decimal", point.DataType);
            Assert.Equal(ByteOrder.BigEndian, point.DecodeOptions.ByteOrder);
            Assert.Equal("v5 兼容/项目初始值", point.ByteOrderSource);
            Assert.Equal(0m, point.RawMin);
            Assert.Equal(120m, point.EffectiveEngMax);
            Assert.False(point.IsWritable);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task CsvImport_ReportsDuplicateCodeAndAddressWithoutSaving()
    {
        var path = TempFile(".csv");
        try
        {
            var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
            var row = string.Join(',', "AI.P1", "sim.same", "小数", "", "", "只读", "", "", "", "", "");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}\n{row}");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("点位编码重复", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Message.Contains("同一设备的地址参数重复", StringComparison.Ordinal));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task ExcelImport_ReadsFirstWorksheet()
    {
        var path = TempFile(".xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet(DevicePointTemplateDefinition.DataSheetName);
                var headers = DevicePointTemplateDefinition.Columns.Select(column => column.Header).ToArray();
                for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
                sheet.Cell(2, 1).Value = "AI.压力";
                sheet.Cell(2, 2).Value = "sim.pressure";
                sheet.Cell(2, 3).Value = "小数";
                sheet.Cell(2, 6).Value = "只读";
                workbook.SaveAs(path);
            }

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.Equal("AI.压力", Assert.Single(result.Points).Code);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task EnglishHeadersAreRejectedByFixedTemplateContract()
    {
        var path = TempFile(".csv");
        try
        {
            await File.WriteAllTextAsync(path,
                "Tag Name,Address,Data Type,Respect Data Type,Client Access,Scan Rate,Scaling,Raw Low,Raw High,Scaled Low,Scaled High,Scaled Data Type,Clamp Low,Clamp High,Eng Units,Description,Negate Value\n"
                + "Input.Fault,H402102.3,Boolean,1,R/W,100,,,,,,,,,,,");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("模板列数量不正确", StringComparison.Ordinal));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task ExportedTemplateCanBeFilledAndImported()
    {
        var path = TempFile(".xlsx");
        try
        {
            await new DevicePointTemplateExporter().ExportAsync(path);
            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            for (var i = 0; i < DevicePointTemplateDefinition.Columns.Count; i++)
                Assert.Equal(DevicePointTemplateDefinition.Columns[i].Header, sheet.Cell(1, i + 1).GetString());
                Assert.Equal(4, sheet.DataValidations.Count());
                Assert.True(sheet.Cell(1, 1).HasComment);
                Assert.Equal(DevicePointTemplateDefinition.ExamplesSheetName, workbook.Worksheet(3).Name);
                Assert.Equal("试验压力", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 1).GetString().Split('.').Last());
                sheet.Cell(2, 1).Value = "AI.模板压力";
                sheet.Cell(2, 2).Value = "sim.template";
                sheet.Cell(2, 3).Value = "小数";
                sheet.Cell(2, 6).Value = "读写";
                sheet.Cell(2, 7).Value = 0;
                sheet.Cell(2, 8).Value = 10000;
                sheet.Cell(2, 9).Value = 0;
                sheet.Cell(2, 10).Value = 10;
                sheet.Cell(2, 11).Value = "由模板生成";
                workbook.Save();
            }

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            var point = Assert.Single(result.Points);
            Assert.Equal("AI.模板压力", point.Code);
            Assert.True(point.IsWritable);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task UnsupportedCustomerChoiceIsRejectedBeforeSave()
    {
        var path = TempFile(".csv");
        try
        {
            var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
             var row = string.Join(',', "AI.P1", "sim.p1", "现场类型", "", "", "只读", "", "", "", "", "");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("原始数据类型不受支持", StringComparison.Ordinal));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V6CsvImport_PreservesModbusAddressAndDecodeOptions()
    {
        var path = TempFile(".csv");
        try
        {
            var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
            var row = string.Join(',',
                "DEV_MODBUS/AI.温度",
                "40001",
                "浮点型",
                "大端",
                "高字在前",
                "只读",
                "",
                "",
                "",
                "",
                "保持寄存器温度");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            var point = Assert.Single(result.Points);
            Assert.Equal("HR:0", point.Address);
            Assert.Equal("40001", point.OriginalAddress);
            Assert.Equal("HR", point.AddressDefinition?.Area);
            Assert.Equal(0, point.AddressDefinition?.Offset);
            Assert.Equal(ByteOrder.BigEndian, point.DecodeOptions.ByteOrder);
            Assert.Equal(WordOrder.HighWordFirst, point.DecodeOptions.WordOrder);
            Assert.Equal("显式填写", point.ByteOrderSource);
            Assert.Equal("显式填写", point.WordOrderSource);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task LegacyV5ModbusMultiRegisterRow_IsRejectedWithoutGuessingWordOrder()
    {
        var path = TempFile(".csv");
        try
        {
            await File.WriteAllTextAsync(path,
                "点位标签,地址,数据类型,访问权限,原始下限,原始上限,工程下限,工程上限,说明\n"
                + "AI.温度,40001,浮点型,只读,,,,,旧模板");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("不能自动猜测", StringComparison.Ordinal));
        }
        finally { TryDelete(path); }
    }

    private static string TempFile(string extension)
        => Path.Combine(Path.GetTempPath(), "testbench-point-import-" + Guid.NewGuid().ToString("N") + extension);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
