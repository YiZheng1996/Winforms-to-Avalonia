using ClosedXML.Excel;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Reports;

/// <summary>
/// ClosedXML 报表生成器（固定版式）：模板存在时加载并覆盖 C2:E5 头部与第 9 行起项点表，
/// 否则创建标准版式。只读已保存的报表数据，不读取 Excel 作为输入源。
/// </summary>
public sealed class ClosedXmlReportGenerator : IReportGenerator
{
    private const string SheetName = "报表";
    private const int TableHeaderRow = 8;
    private const int TableFirstDataRow = 9;

    public async Task<string> GenerateAsync(ReportData data, string templatePath, string outputDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"报表_{data.TaskNumber}_{data.RecordId}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

        await Task.Run(() =>
        {
            using var workbook = File.Exists(templatePath) ? new XLWorkbook(templatePath) : CreateStandardWorkbook(data);
            var sheet = workbook.Worksheets.Contains(SheetName) ? workbook.Worksheet(SheetName) : workbook.AddWorksheet(SheetName);

            sheet.Cell("C2").Value = data.TaskNumber;
            sheet.Cell("C3").Value = data.ProductNumber;
            sheet.Cell("C4").Value = data.ProductModelCode;
            sheet.Cell("C5").Value = data.RecipeVersionNumber;
            sheet.Cell("E2").Value = data.DeviceMode;
            sheet.Cell("E3").Value = data.OperatorName;
            sheet.Cell("E4").Value = data.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cell("E5").Value = data.Conclusion;

            sheet.Cell(TableHeaderRow, 1).Value = "序号";
            sheet.Cell(TableHeaderRow, 2).Value = "项点";
            sheet.Cell(TableHeaderRow, 3).Value = "摘要值";
            sheet.Cell(TableHeaderRow, 4).Value = "结果说明";
            sheet.Cell(TableHeaderRow, 5).Value = "判定";
            for (var i = 0; i < data.Items.Count; i++)
            {
                var row = TableFirstDataRow + i;
                var item = data.Items[i];
                sheet.Cell(row, 1).Value = item.SortOrder;
                sheet.Cell(row, 2).Value = item.ItemName;
                sheet.Cell(row, 3).Value = item.SummaryValue;
                sheet.Cell(row, 4).Value = item.ResultText;
                sheet.Cell(row, 5).Value = item.State;
            }
            workbook.SaveAs(outputPath);
        }, ct);

        return outputPath;
    }

    /// <summary>
    /// 生成标准模板（固定版式，无数据），供管理员准备阶段使用。
    /// </summary>
    public static void CreateStandardTemplate(string templatePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(templatePath))!);
        using var workbook = CreateStandardWorkbook(null);
        workbook.SaveAs(templatePath);
    }

    private static XLWorkbook CreateStandardWorkbook(ReportData? data)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName);
        sheet.Cell("A1").Value = "试验报表";
        sheet.Cell("B2").Value = "任务编号";
        sheet.Cell("C2").Value = data?.TaskNumber ?? string.Empty;
        sheet.Cell("B3").Value = "产品编号";
        sheet.Cell("C3").Value = data?.ProductNumber ?? string.Empty;
        sheet.Cell("B4").Value = "产品型号";
        sheet.Cell("C4").Value = data?.ProductModelCode ?? string.Empty;
        sheet.Cell("B5").Value = "配方版本";
        sheet.Cell("C5").Value = data?.RecipeVersionNumber ?? string.Empty;
        sheet.Cell("D2").Value = "设备模式";
        sheet.Cell("E2").Value = data?.DeviceMode ?? string.Empty;
        sheet.Cell("D3").Value = "操作员";
        sheet.Cell("E3").Value = data?.OperatorName ?? string.Empty;
        sheet.Cell("D4").Value = "开始时间";
        sheet.Cell("E4").Value = data?.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
        sheet.Cell("D5").Value = "总体结论";
        sheet.Cell("E5").Value = data?.Conclusion ?? string.Empty;
        return workbook;
    }
}
