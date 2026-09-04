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
            await File.WriteAllTextAsync(path, "点位编码,点位名称,协议,地址,数据类型,单位,原始下限,原始上限,工程下限,工程上限,可写,风险等级,启用,说明\nAI_Temp,温度,Simulation,sim.temp,Decimal,℃,0,10000,-40,120,否,普通,是,温度输入");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            var point = Assert.Single(result.Points);
            Assert.Equal("AI_Temp", point.Code);
            Assert.Equal("温度", point.Name);
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
            var row = string.Join(',', "P1", "", "Simulation", "sim.same", "Decimal", "", "", "", "", "", "否", "普通", "是", "");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}\n{row}");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("点位编码重复", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Message.Contains("协议/地址组合重复", StringComparison.Ordinal));
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
                sheet.Cell(2, 1).Value = "AI_Pressure";
                sheet.Cell(2, 2).Value = "压力";
                sheet.Cell(2, 3).Value = "Simulation";
                sheet.Cell(2, 4).Value = "sim.pressure";
                sheet.Cell(2, 5).Value = "Decimal";
                sheet.Cell(2, 11).Value = "否";
                sheet.Cell(2, 12).Value = "普通";
                sheet.Cell(2, 13).Value = "是";
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
                sheet.Cell(2, 1).Value = "AI_Template";
                sheet.Cell(2, 2).Value = "模板压力";
                sheet.Cell(2, 3).Value = "Simulation";
                sheet.Cell(2, 4).Value = "sim.template";
                sheet.Cell(2, 5).Value = "Decimal";
                sheet.Cell(2, 6).Value = "MPa";
                sheet.Cell(2, 7).Value = 0;
                sheet.Cell(2, 8).Value = 10000;
                sheet.Cell(2, 9).Value = 0;
                sheet.Cell(2, 10).Value = 10;
                sheet.Cell(2, 11).Value = "否";
                sheet.Cell(2, 12).Value = "普通";
                sheet.Cell(2, 13).Value = "是";
                sheet.Cell(2, 14).Value = "由模板生成";
                workbook.Save();
            }

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.Equal("AI_Template", Assert.Single(result.Points).Code);
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
