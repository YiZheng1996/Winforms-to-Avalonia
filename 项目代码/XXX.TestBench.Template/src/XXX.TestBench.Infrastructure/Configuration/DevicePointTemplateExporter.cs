using ClosedXML.Excel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 设备点位 Excel 导入模板生成器。
/// </summary>
public sealed class DevicePointTemplateExporter : IDevicePointTemplateExporter
{
    /// <summary>
    /// 生成模板；Excel 写盘放到后台线程，避免阻塞界面。
    /// </summary>
    public Task ExportAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => ExportCore(filePath, ct), ct);

    /// <summary>
    /// 创建带中文固定表头和填写说明的工作簿。
    /// </summary>
    private static void ExportCore(string filePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("模板保存路径不能为空", nameof(filePath));
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("设备点位模板只能保存为 .xlsx 文件");

        var columns = DevicePointTemplateDefinition.Columns;
        using var workbook = new XLWorkbook();
        var dataSheet = workbook.AddWorksheet(DevicePointTemplateDefinition.DataSheetName);

        for (var index = 0; index < columns.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = dataSheet.Cell(1, index + 1);
            cell.Value = columns[index].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1769AA");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }

        var headerRange = dataSheet.Range(1, 1, 1, columns.Count);
        headerRange.SetAutoFilter();
        dataSheet.SheetView.FreezeRows(1);
        dataSheet.Row(1).Height = 32;

        var widths = new[] { 18, 18, 18, 24, 16, 12, 14, 14, 14, 14, 12, 14, 12, 28 };
        for (var index = 0; index < widths.Length; index++)
            dataSheet.Column(index + 1).Width = widths[index];

        var inputRange = dataSheet.Range(2, 1, 1001, columns.Count);
        inputRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        inputRange.Style.Alignment.WrapText = false;
        dataSheet.Range(2, 7, 1001, 10).Style.NumberFormat.Format = "0.###############";

        var instructions = workbook.AddWorksheet(DevicePointTemplateDefinition.InstructionsSheetName);
        instructions.Column(1).Width = 18;
        instructions.Column(2).Width = 18;
        instructions.Column(3).Width = 72;
        instructions.Row(1).Height = 30;
        instructions.Range(1, 1, 1, 3).Merge();
        instructions.Cell(1, 1).Value = "设备点位导入模板填写说明";
        instructions.Cell(1, 1).Style.Font.Bold = true;
        instructions.Cell(1, 1).Style.Font.FontSize = 16;
        instructions.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        instructions.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1769AA");
        instructions.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        instructions.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        instructions.Cell(3, 1).Value = "填写规则";
        instructions.Cell(3, 1).Style.Font.Bold = true;
        instructions.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#1769AA");
        var rules = new[]
        {
            "1. 只在“点位模板”工作表中填写数据，从第2行开始一行一个点位。",
            "2. 第1行列名和列顺序是固定格式，不允许修改、删除、增加或调换。",
            "3. 必填数据：点位编码、协议、地址、数据类型；点位编码和协议/地址组合不能重复。",
            "4. 可写、启用填写“是”或“否”；风险等级填写“普通”或“高风险”。",
            "5. 原始下限、原始上限、工程下限、工程上限必须全部填写或全部留空。",
            "6. 导入成功后会整体替换当前点位目录；校验失败时不会保存任何一行。"
        };
        for (var index = 0; index < rules.Length; index++)
        {
            instructions.Range(4 + index, 1, 4 + index, 3).Merge();
            instructions.Cell(4 + index, 1).Value = rules[index];
            instructions.Cell(4 + index, 1).Style.Alignment.WrapText = true;
        }

        var startRow = 12;
        instructions.Cell(startRow, 1).Value = "列名";
        instructions.Cell(startRow, 2).Value = "是否必填";
        instructions.Cell(startRow, 3).Value = "填写说明 / 示例";
        var instructionHeader = instructions.Range(startRow, 1, startRow, 3);
        instructionHeader.Style.Font.Bold = true;
        instructionHeader.Style.Font.FontColor = XLColor.White;
        instructionHeader.Style.Fill.BackgroundColor = XLColor.FromHtml("#1769AA");
        instructionHeader.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        for (var index = 0; index < columns.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var row = startRow + index + 1;
            var column = columns[index];
            instructions.Cell(row, 1).Value = column.Header;
            instructions.Cell(row, 2).Value = column.IsRequired ? "是" : "否";
            instructions.Cell(row, 3).Value = $"{column.Description}；示例：{column.Example}";
            instructions.Cell(row, 3).Style.Alignment.WrapText = true;
        }

        instructions.SheetView.FreezeRows(startRow);
        workbook.SaveAs(filePath);
    }
}
