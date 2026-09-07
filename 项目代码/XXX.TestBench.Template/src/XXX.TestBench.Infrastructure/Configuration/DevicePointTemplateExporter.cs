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
    /// 创建带中文固定表头、下拉选项、填写示例和填写说明的工作簿。
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

        WriteHeader(dataSheet, columns);
        ConfigureDataSheet(dataSheet, columns);
        AddChoiceValidations(dataSheet);

        var instructions = workbook.AddWorksheet(DevicePointTemplateDefinition.InstructionsSheetName);
        WriteInstructions(instructions, columns, ct);

        var examples = workbook.AddWorksheet(DevicePointTemplateDefinition.ExamplesSheetName);
        WriteExamples(examples, columns, ct);

        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// 写入固定中文表头，并给每个表头增加鼠标悬停说明。
    /// </summary>
    private static void WriteHeader(IXLWorksheet worksheet, IReadOnlyList<DevicePointTemplateDefinition.ColumnDefinition> columns)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var cell = worksheet.Cell(1, index + 1);
            cell.Value = column.Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1769AA");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;

            var comment = cell.CreateComment();
            comment.AddText($"{column.Description}\n示例：{column.Example}");
            comment.SetAuthor("设备点位模板");
        }
    }

    /// <summary>
    /// 设置主填写页的颜色、冻结、筛选和数字格式。
    /// </summary>
    private static void ConfigureDataSheet(
        IXLWorksheet worksheet,
        IReadOnlyList<DevicePointTemplateDefinition.ColumnDefinition> columns)
    {
        var headerRange = worksheet.Range(1, 1, 1, columns.Count);
        headerRange.SetAutoFilter();
        worksheet.SheetView.FreezeRows(1);
        worksheet.Row(1).Height = 36;

        var widths = new[] { 38, 18, 18, 18, 18, 28, 20, 20, 14, 14, 12, 14, 14, 14, 14, 10, 10, 12, 30 };
        for (var index = 0; index < columns.Count; index++)
            worksheet.Column(index + 1).Width = widths[index];

        var inputRange = worksheet.Range(
            DevicePointTemplateDefinition.FirstDataRow,
            1,
            DevicePointTemplateDefinition.LastDataRow,
            columns.Count);
        inputRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFBEA");
        inputRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        inputRange.Style.Alignment.WrapText = false;
        worksheet.Range(
                DevicePointTemplateDefinition.FirstDataRow,
                ColumnIndex(columns, "RawMin"),
                DevicePointTemplateDefinition.LastDataRow,
                ColumnIndex(columns, "EngMax"))
            .Style.NumberFormat.Format = "0.###############";
    }

    /// <summary>
    /// 为容易填错的列增加 Excel 下拉选项和错误提示。
    /// </summary>
    private static void AddChoiceValidations(IXLWorksheet worksheet)
    {
        var firstRow = DevicePointTemplateDefinition.FirstDataRow;
        var lastRow = DevicePointTemplateDefinition.LastDataRow;
        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "AddressType"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "AddressType")),
            DevicePointTemplateDefinition.AddressTypeChoices.Select(choice => choice.DisplayValue),
            "请从下拉框选择地址类型");
        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "RawDataType"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "RawDataType")),
            DevicePointTemplateDefinition.RawDataTypeChoices.Select(choice => choice.DisplayValue),
            "请从下拉框选择原始数据类型");
        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "ByteOrder"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "ByteOrder")),
            DevicePointTemplateDefinition.ByteOrderChoices.Select(choice => choice.DisplayValue),
            "请从下拉框选择字节序");
        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "IsWritable"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "IsEnabled")),
            DevicePointTemplateDefinition.YesNoChoices,
            "请填写 是 或 否");
        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "RiskLevel"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "RiskLevel")),
            new[] { "普通", "高风险" },
            "请填写 普通 或 高风险");
    }

    /// <summary>
    /// 添加显式列表校验；空值仍允许保留给非必填列的默认规则。
    /// </summary>
    private static void AddListValidation(IXLRange range, IEnumerable<string> values, string errorMessage)
    {
        var validation = range.CreateDataValidation();
        validation.IgnoreBlanks = true;
        validation.InCellDropdown = true;
        validation.List($"\"{string.Join(",", values)}\"", true);
        validation.ShowErrorMessage = true;
        validation.ErrorTitle = "填写值不合法";
        validation.ErrorMessage = errorMessage;
        validation.ShowInputMessage = true;
        validation.InputTitle = "请选择模板值";
        validation.InputMessage = errorMessage;
    }

    /// <summary>
    /// 写入规则、字段说明和客户可选值，减少客户猜测单元格含义的成本。
    /// </summary>
    private static void WriteInstructions(
        IXLWorksheet worksheet,
        IReadOnlyList<DevicePointTemplateDefinition.ColumnDefinition> columns,
        CancellationToken ct)
    {
        worksheet.Column(1).Width = 20;
        worksheet.Column(2).Width = 18;
        worksheet.Column(3).Width = 76;
        worksheet.Row(1).Height = 30;
        worksheet.Range(1, 1, 1, 3).Merge();
        worksheet.Cell(1, 1).Value = "设备点位导入模板填写说明";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;
        worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        worksheet.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1769AA");
        worksheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        worksheet.Cell(3, 1).Value = "先看这里";
        worksheet.Cell(3, 1).Style.Font.Bold = true;
        worksheet.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#1769AA");
        var rules = new[]
        {
            "1. 最简单的填写方式：先打开“填写示例”，按所属设备和驱动示例复制一行，再回到“点位模板”逐行替换成真实点位。",
            "2. 只在“点位模板”工作表中填写数据，从第2行开始一行一个点位；“填写示例”不能直接导入。",
            "3. 第1行列名和列顺序是固定格式，不允许修改、删除、增加或调换，否则导入会被拒绝。",
            "4. 黄色区域是填写区；带下拉箭头的单元格请直接选择，不要自行改写成其他说法。",
            "5. 必填数据：点位编码、设备编码、地址类型、地址参数、原始数据类型；设备必须已在设备配置中存在。点位分组编码可留空，自动归入该设备的 DEFAULT/未分组。",
            "6. 原始下限、原始上限、工程下限、工程上限必须全部填写或全部留空；没有量程时留空即可。",
            "7. 点位标识新增时可留空；更新时优先按点位标识匹配，并核对设备编码和点位编码。",
            "8. 可写=否时表示只读；启动、停止、复位、输出等动作请选择“是 + 高风险”。",
            "9. 点位分组必须是设备内已存在的分组；不会通过导入文件隐式创建设备或分组。分组只用于树状界面筛选，不改变地址和运行时路由。",
            "10. 导入采用合并新增/更新，文件中未出现的其他设备点位保持不变；确认前会显示预览，并标出更新分组。",
            "11. 点位地址按驱动规则填写；Modbus 偏移统一从零开始，不能直接把 40001 当作协议偏移。"
        };
        for (var index = 0; index < rules.Length; index++)
        {
            worksheet.Range(4 + index, 1, 4 + index, 3).Merge();
            worksheet.Cell(4 + index, 1).Value = rules[index];
            worksheet.Cell(4 + index, 1).Style.Alignment.WrapText = true;
        }

        var startRow = 14;
        worksheet.Cell(startRow, 1).Value = "列名";
        worksheet.Cell(startRow, 2).Value = "是否必填";
        worksheet.Cell(startRow, 3).Value = "填写说明 / 示例";
        StyleTableHeader(worksheet.Range(startRow, 1, startRow, 3));

        for (var index = 0; index < columns.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var row = startRow + index + 1;
            var column = columns[index];
            worksheet.Cell(row, 1).Value = column.Header;
            worksheet.Cell(row, 2).Value = column.IsRequired ? "是" : "否";
            worksheet.Cell(row, 3).Value = $"{column.Description}；示例：{column.Example}";
            worksheet.Cell(row, 3).Style.Alignment.WrapText = true;
        }

        var choiceStart = startRow + columns.Count + 3;
        worksheet.Cell(choiceStart, 1).Value = "下拉值怎么理解";
        worksheet.Cell(choiceStart, 1).Style.Font.Bold = true;
        worksheet.Cell(choiceStart, 1).Style.Font.FontColor = XLColor.FromHtml("#1769AA");
        worksheet.Cell(choiceStart + 1, 1).Value = "字段";
        worksheet.Cell(choiceStart + 1, 2).Value = "可选值";
        worksheet.Cell(choiceStart + 1, 3).Value = "客户填写含义";
        StyleTableHeader(worksheet.Range(choiceStart + 1, 1, choiceStart + 1, 3));

        var choices = DevicePointTemplateDefinition.AddressTypeChoices
            .Select(choice => ("地址类型", choice.DisplayValue, choice.Description))
            .Concat(DevicePointTemplateDefinition.RawDataTypeChoices
                .Select(choice => ("原始数据类型", choice.DisplayValue, choice.Description)))
            .Concat(new[]
            {
                ("字节序", "大端 / 小端", "多字节数据的字节排列顺序，必须按设备手册确认。"),
                ("字序", "无 / 高字在前 / 低字在前", "32 位数据的寄存器顺序，不适用时填写“无”。"),
                ("可写 / 启用", "是 / 否", "可写=允许受控写入；启用=参与运行。"),
                ("风险等级", "普通 / 高风险", "高风险用于启动、停止、复位、输出等可能改变设备动作的点位。")
            }).ToArray();
        for (var index = 0; index < choices.Length; index++)
        {
            var row = choiceStart + index + 2;
            worksheet.Cell(row, 1).Value = choices[index].Item1;
            worksheet.Cell(row, 2).Value = choices[index].Item2;
            worksheet.Cell(row, 3).Value = choices[index].Item3;
            worksheet.Cell(row, 3).Style.Alignment.WrapText = true;
        }

        worksheet.SheetView.FreezeRows(startRow);
    }

    /// <summary>
    /// 写入可直接照抄的示例页，避免把示例行混入真正的导入数据。
    /// </summary>
    private static void WriteExamples(
        IXLWorksheet worksheet,
        IReadOnlyList<DevicePointTemplateDefinition.ColumnDefinition> columns,
        CancellationToken ct)
    {
        worksheet.TabColor = XLColor.FromHtml("#52A36A");
        var widths = new[] { 38, 18, 18, 18, 18, 28, 20, 20, 14, 14, 12, 14, 14, 14, 14, 10, 10, 12, 30 };
        for (var index = 0; index < columns.Count; index++)
            worksheet.Column(index + 1).Width = widths[index];

        for (var index = 0; index < columns.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = worksheet.Cell(1, index + 1);
            cell.Value = columns[index].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#52A36A");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }

        var rows = new[]
        {
            new[] { "9d2c2a1e-5d87-4d3a-b4fd-2a3f7fb4e901", "AI_Pressure", "试验压力", "PLC_SIM_1", "PRESSURE", "仿真逻辑地址", "sim.pressure", "小数", "大端", "无", "MPa", "0", "10000", "0", "10", "否", "是", "普通", "采集压力示例" },
            new[] { "1aeac75f-6c6c-4a18-8c11-4d8a38a9f514", "DO_Start", "启动命令", "PLC_SIM_1", "COMMAND", "仿真逻辑地址", "sim.start", "布尔量", "大端", "无", "", "", "", "", "", "是", "是", "高风险", "高风险写入示例" },
            new[] { "3f2a4a67-bb4d-4a8f-8c92-3c5b20f7ef2f", "AI_Temperature", "环境温度", "PLC_SIM_1", "ENV", "仿真逻辑地址", "sim.temperature", "小数", "大端", "无", "℃", "-400", "1200", "-40", "120", "否", "是", "普通", "采集温度示例" }
        };
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value = rows[rowIndex][columnIndex];
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1FAF3");
            }
        }

        var noteRow = rows.Length + 4;
        worksheet.Range(noteRow, 1, noteRow, columns.Count).Merge();
        worksheet.Cell(noteRow, 1).Value = "说明：示例只用于理解填写方式；请把真实点位复制到“点位模板”工作表后再导入。"
            + " 当前模板的地址示例为仿真逻辑地址，不会连接现场设备。";
        worksheet.Cell(noteRow, 1).Style.Font.FontColor = XLColor.FromHtml("#7A5B00");
        worksheet.Cell(noteRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF9EB");
        worksheet.Cell(noteRow, 1).Style.Alignment.WrapText = true;
        worksheet.Row(noteRow).Height = 34;
        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, 1, columns.Count).SetAutoFilter();
    }

    private static int ColumnIndex(
        IReadOnlyList<DevicePointTemplateDefinition.ColumnDefinition> columns,
        string field)
        => columns.Select((column, index) => (column, index))
            .First(item => string.Equals(item.column.Field, field, StringComparison.Ordinal)).index + 1;

    /// <summary>
    /// 设置说明页表头样式。
    /// </summary>
    private static void StyleTableHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#1769AA");
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }
}
