using ClosedXML.Excel;
using XXX.TestBench.Core.Configuration;
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
            await File.WriteAllTextAsync(path, "点位编码,点位名称,通信方式,设备地址,数据类型,工程单位,原始下限,原始上限,工程下限,工程上限,是否允许写入,写入风险,是否启用,说明\nAI_Temp,温度,仿真,sim.temp,小数,℃,0,10000,-40,120,否,普通,是,温度输入");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            var point = Assert.Single(result.Points);
            Assert.Equal("AI_Temp", point.Code);
            Assert.Equal("温度", point.Name);
            Assert.Equal("Simulation", point.Protocol);
            Assert.Equal("Decimal", point.DataType);
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
            var row = string.Join(',', "", "P1", "压力", "PLC1", "", "仿真逻辑地址", "sim.same", "小数", "大端", "无", "", "", "", "", "", "否", "是", "普通", "");
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
                sheet.Cell(2, 2).Value = "AI_Pressure";
                sheet.Cell(2, 3).Value = "压力";
                sheet.Cell(2, 4).Value = "PLC1";
                sheet.Cell(2, 6).Value = "仿真逻辑地址";
                sheet.Cell(2, 7).Value = "sim.pressure";
                sheet.Cell(2, 8).Value = "小数";
                sheet.Cell(2, 9).Value = "大端";
                sheet.Cell(2, 10).Value = "无";
                sheet.Cell(2, 16).Value = "否";
                sheet.Cell(2, 17).Value = "是";
                sheet.Cell(2, 18).Value = "普通";
                workbook.SaveAs(path);
            }

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.Equal("AI_Pressure", Assert.Single(result.Points).Code);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task EnglishHeadersAreRejectedByFixedTemplateContract()
    {
        var path = TempFile(".csv");
        try
        {
            await File.WriteAllTextAsync(path, "Code,Name,Protocol,Address,DataType,Unit,RawMin,RawMax,EngMin,EngMax,IsWritable,RiskLevel,IsEnabled,Description\nP1,,Simulation,sim.same,Decimal,,,,,否,普通,是");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("模板第1行第 1 列应为", StringComparison.Ordinal));
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
                Assert.Equal(5, sheet.DataValidations.Count());
                Assert.True(sheet.Cell(1, 3).HasComment);
                Assert.Equal(DevicePointTemplateDefinition.ExamplesSheetName, workbook.Worksheet(3).Name);
                Assert.Equal("试验压力", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 3).GetString());
                sheet.Cell(2, 1).Value = "";
                sheet.Cell(2, 2).Value = "AI_Template";
                sheet.Cell(2, 3).Value = "模板压力";
                sheet.Cell(2, 4).Value = "PLC1";
                sheet.Cell(2, 5).Value = "DEFAULT";
                sheet.Cell(2, 6).Value = "仿真逻辑地址";
                sheet.Cell(2, 7).Value = "sim.template";
                sheet.Cell(2, 8).Value = "小数";
                sheet.Cell(2, 9).Value = "大端";
                sheet.Cell(2, 10).Value = "无";
                sheet.Cell(2, 11).Value = "MPa";
                sheet.Cell(2, 12).Value = 0;
                sheet.Cell(2, 13).Value = 10000;
                sheet.Cell(2, 14).Value = 0;
                sheet.Cell(2, 15).Value = 10;
                sheet.Cell(2, 16).Value = "否";
                sheet.Cell(2, 17).Value = "是";
                sheet.Cell(2, 18).Value = "普通";
                sheet.Cell(2, 19).Value = "由模板生成";
                workbook.Save();
            }

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.Equal("AI_Template", Assert.Single(result.Points).Code);
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
            var row = string.Join(',', "", "P1", "", "PLC1", "", "现场协议", "40001", "小数", "大端", "无", "", "", "", "", "", "否", "是", "普通", "");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("地址类型不受支持", StringComparison.Ordinal));
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
