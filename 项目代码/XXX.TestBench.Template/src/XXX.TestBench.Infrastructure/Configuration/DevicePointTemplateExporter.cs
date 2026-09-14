using ClosedXML.Excel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
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
        => Task.Run(() => ExportCore(filePath, request: null, ct), ct);

    /// <summary>
    /// 按设备、通信方式或当前页面范围生成模板；Excel 写盘放到后台线程，避免阻塞界面。
    /// </summary>
    public Task ExportAsync(
        string filePath,
        DevicePointTemplateExportRequest request,
        CancellationToken ct = default)
        => Task.Run(() => ExportCore(filePath, request, ct), ct);

    /// <summary>
    /// 创建带中文固定表头、下拉选项、填写示例和填写说明的工作簿。
    /// </summary>
    private static void ExportCore(
        string filePath,
        DevicePointTemplateExportRequest? request,
        CancellationToken ct)
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
        AddChoiceValidations(dataSheet, request);
        WriteScopeComments(dataSheet, request);

        var instructions = workbook.AddWorksheet(DevicePointTemplateDefinition.InstructionsSheetName);
        WriteInstructions(instructions, columns, request, ct);

        var examples = workbook.AddWorksheet(DevicePointTemplateDefinition.ExamplesSheetName);
        WriteExamples(examples, columns, request, ct);

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

        for (var index = 0; index < columns.Count; index++)
            worksheet.Column(index + 1).Width = columns[index].Field switch
            {
                "PointTag" => 34,
                "Address" => 24,
                "DataType" => 18,
                "ByteOrder" or "WordOrder" => 20,
                "Access" => 18,
                "RawMin" or "RawMax" or "EngMin" or "EngMax" => 14,
                "Description" => 36,
                _ => 18
            };

        var inputRange = worksheet.Range(
            DevicePointTemplateDefinition.FirstDataRow,
            1,
            DevicePointTemplateDefinition.LastDataRow,
            columns.Count);
        inputRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFBEA");
        inputRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        inputRange.Style.Alignment.WrapText = false;
        var rawMinIndex = columns.Select((column, index) => (column.Field, index))
            .Where(item => item.Field == "RawMin")
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        var engMaxIndex = columns.Select((column, index) => (column.Field, index))
            .Where(item => item.Field == "EngMax")
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        if (rawMinIndex >= 0 && engMaxIndex >= rawMinIndex)
        {
            worksheet.Range(
                    DevicePointTemplateDefinition.FirstDataRow,
                    rawMinIndex + 1,
                    DevicePointTemplateDefinition.LastDataRow,
                    engMaxIndex + 1)
                .Style.NumberFormat.Format = "0.###############";
        }
    }

    /// <summary>
    /// 为容易填错的列增加 Excel 下拉选项和错误提示。
    /// </summary>
    private static void AddChoiceValidations(
        IXLWorksheet worksheet,
        DevicePointTemplateExportRequest? request)
    {
        var firstRow = DevicePointTemplateDefinition.FirstDataRow;
        var lastRow = DevicePointTemplateDefinition.LastDataRow;
        var rawDataChoices = ResolveRawDataChoices(request);

        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "DataType"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "DataType")),
            rawDataChoices.Select(choice => choice.DisplayValue),
            "请从下拉框选择原始数据类型");
        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "Access"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "Access")),
            DevicePointTemplateDefinition.AccessChoices.Select(choice => choice.DisplayValue),
            "请从下拉框选择 只读或读写");
        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "ByteOrder"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "ByteOrder")),
            DevicePointTemplateDefinition.ByteOrderChoices.Select(choice => choice.DisplayValue),
            "Modbus 寄存器点位可选择大端或小端；留空采用项目初始大端，其他协议可留空");
        AddListValidation(
            worksheet.Range(firstRow, ColumnIndex(DevicePointTemplateDefinition.Columns, "WordOrder"), lastRow,
                ColumnIndex(DevicePointTemplateDefinition.Columns, "WordOrder")),
            DevicePointTemplateDefinition.WordOrderChoices.Select(choice => choice.DisplayValue),
            "Modbus 点位按类型选择 None、HighWordFirst 或 LowWordFirst；其他协议可留空");
    }

    /// <summary>
    /// 根据已注册驱动能力收窄原始数据类型；无法取得能力时保留完整中文选项。
    /// </summary>
    private static IReadOnlyList<DevicePointTemplateDefinition.TemplateChoice> ResolveRawDataChoices(
        DevicePointTemplateExportRequest? request)
    {
        if (request?.Targets is not { Count: > 0 })
            return DevicePointTemplateDefinition.RawDataTypeChoices;

        var supported = request.Targets
            .SelectMany(target => target.SupportedDataTypes)
            .ToHashSet();
        if (supported.Count == 0)
            return DevicePointTemplateDefinition.RawDataTypeChoices;

        var choices = DevicePointTemplateDefinition.RawDataTypeChoices
            .Where(choice => DevicePointTemplateDefinition.TryMapDataType(choice.DisplayValue, out var type)
                && supported.Contains(type))
            .ToList();
        return choices.Count == 0 ? DevicePointTemplateDefinition.RawDataTypeChoices : choices;
    }

    /// <summary>
    /// 在表头批注中说明模板范围，客户打开表格时无需猜测设备选择规则。
    /// </summary>
    private static void WriteScopeComments(
        IXLWorksheet worksheet,
        DevicePointTemplateExportRequest? request)
    {
        if (request is null || request.Targets.Count == 0)
            return;

        var targetText = string.Join("、", request.Targets.Select(target =>
            $"{target.DisplayName}（{target.ProtocolDisplayName}）"));
        var tagCell = worksheet.Cell(1, ColumnIndex(DevicePointTemplateDefinition.Columns, "PointTag"));
        var tagRule = request.Targets.Count > 1
            ? "本模板包含多个设备，点位标签请使用“设备编码/分组.点位”。"
            : "本模板只对应一个设备，点位标签填写“分组.点位”即可。";
        tagCell.CreateComment().AddText(
            $"模板范围：{request.ScopeDisplayName}\n{request.ScopeDescription}\n{tagRule}\n目标设备：{targetText}");
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
        DevicePointTemplateExportRequest? request,
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

        worksheet.Range(3, 1, 3, 3).Merge();
        worksheet.Cell(3, 1).Value = "本模板适用范围";
        worksheet.Cell(3, 1).Style.Font.Bold = true;
        worksheet.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#1769AA");

        var scopeText = request is null || request.Targets.Count == 0
            ? "通用设备点位模板；填写时请按模板说明使用“分组.点位”标签。"
            : $"已按“{request.ScopeDisplayName}”准备。{request.ScopeDescription}";
        worksheet.Range(4, 1, 4, 3).Merge();
        worksheet.Cell(4, 1).Value = scopeText;
        worksheet.Cell(4, 1).Style.Alignment.WrapText = true;

        var targetText = request is null || request.Targets.Count == 0
            ? "设备范围：由当前页面选择范围确定；跨设备模板会在点位标签中保留设备编码前缀。"
            : "设备范围：" + string.Join("、", request.Targets.Select(target =>
                $"{target.DisplayName}（{target.ProtocolDisplayName}）"));
        worksheet.Range(5, 1, 5, 3).Merge();
        worksheet.Cell(5, 1).Value = targetText;
        worksheet.Cell(5, 1).Style.Alignment.WrapText = true;

        worksheet.Range(7, 1, 7, 3).Merge();
        worksheet.Cell(7, 1).Value = "先看这里";
        worksheet.Cell(7, 1).Style.Font.Bold = true;
        worksheet.Cell(7, 1).Style.Font.FontColor = XLColor.FromHtml("#1769AA");
        var rules = new[]
        {
            "1. 最简单的填写方式：先打开“填写示例”，按所属设备和驱动示例复制一行，再回到“点位模板”逐行替换成真实点位。",
            "2. 只在“点位模板”工作表中填写数据，从第2行开始一行一个点位；“填写示例”不能直接导入。",
            "3. 第1行列名和列顺序是固定格式，不允许修改、删除、增加或调换，否则导入会被拒绝。",
            "4. 黄色区域是填写区；带下拉箭头的单元格请直接选择，不要自行改写成其他说法。",
            "5. 必填数据：点位标签、地址、数据类型。单设备模板填写“分组.点位”，未分组时只填写点位名称；跨设备模板填写“设备编码/分组.点位”。",
            "6. 原始下限、原始上限、工程下限、工程上限必须全部填写或全部留空；没有量程时留空即可。",
            "7. 访问权限只填写“只读”或“读写”；启动、停止、复位、输出等动作的安全等级由运行时策略继续校验。",
            "8. 分组是可选的分类方式；导入不会自动创建设备或分组，未分组点位进入系统“未分组”。",
            "9. 当前模板按设备范围和点位标签合并新增/更新，文件中未出现的其他设备点位保持不变；其他版本模板不会被接受。",
            "10. 地址按所属设备驱动和型号填写。Modbus 首选 C:0、DI:0、HR:0、IR:0；S7-1200/1500 示例：DB144.DBD88、DB142.DBX22.3；S7-200 SMART 示例：VW5022、V0.1。格式不匹配会在导入预览前拒绝。",
            "11. 采集周期属于设备配置，不在点位表中重复填写；KEPServer 的 Scan Rate 不会被误当成每个点位的运行时周期。",
            "12. 本模板参考了 KEPServer 的 Tag Name、Address、Data Type、Client Access、Scaling 和 Description 字段，英文表头不能直接导入；项目使用固定中文表头。",
            "13. Modbus 寄存器点位的字节序可填写大端/小端，留空采用项目初始大端并在导入预览标记来源；Int16/UInt16 的字序填 None，Int32/UInt32/Float32/Double 必须明确选择 HighWordFirst 或 LowWordFirst。字节序和字序不能凭经验猜测，请以设备手册为准。",
            "14. 使用西门子 S7 的 DB 绝对地址（例如 DB1.DBD0）时，请先在 TIA Portal 中打开对应数据块，取消“优化的块访问”，重新下载 PLC，再连接或导入。"
        };
        for (var index = 0; index < rules.Length; index++)
        {
            var row = 8 + index;
            worksheet.Range(row, 1, row, 3).Merge();
            worksheet.Cell(row, 1).Value = rules[index];
            worksheet.Cell(row, 1).Style.Alignment.WrapText = true;
        }

        var startRow = 8 + rules.Length + 2;
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

        var choices = ResolveRawDataChoices(request)
            .Select(choice => ("数据类型", choice.DisplayValue, choice.Description))
            .Concat(new[]
            {
                ("访问权限", "只读 / 读写", "客户只填写访问能力；读写点位的风险等级和二次确认由运行时安全策略负责。"),
                ("点位标签", "分组.点位 或 设备编码/分组.点位", "分组前缀只用于分类；跨设备时设备前缀用于确定设备归属。")
            }).ToArray();
        for (var index = 0; index < choices.Length; index++)
        {
            var row = choiceStart + index + 2;
            worksheet.Cell(row, 1).Value = choices[index].Item1;
            worksheet.Cell(row, 2).Value = choices[index].Item2;
            worksheet.Cell(row, 3).Value = choices[index].Item3;
            worksheet.Cell(row, 3).Style.Alignment.WrapText = true;
        }

        var mappingStart = choiceStart + choices.Length + 4;
        worksheet.Range(mappingStart, 1, mappingStart, 3).Merge();
        worksheet.Cell(mappingStart, 1).Value = "参考 CSV 字段如何整理到本项目中文模板";
        worksheet.Cell(mappingStart, 1).Style.Font.Bold = true;
        worksheet.Cell(mappingStart, 1).Style.Font.FontColor = XLColor.FromHtml("#1769AA");
        worksheet.Cell(mappingStart + 1, 1).Value = "参考字段（KEPServer）";
        worksheet.Cell(mappingStart + 1, 2).Value = "项目中文字段";
        worksheet.Cell(mappingStart + 1, 3).Value = "处理规则";
        StyleTableHeader(worksheet.Range(mappingStart + 1, 1, mappingStart + 1, 3));

        for (var index = 0; index < DevicePointTemplateDefinition.ReferenceColumnMappings.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var row = mappingStart + index + 2;
            var mapping = DevicePointTemplateDefinition.ReferenceColumnMappings[index];
            worksheet.Cell(row, 1).Value = mapping.ReferenceHeader;
            worksheet.Cell(row, 2).Value = mapping.ProjectField;
            worksheet.Cell(row, 3).Value = mapping.Handling;
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
        DevicePointTemplateExportRequest? request,
        CancellationToken ct)
    {
        worksheet.TabColor = XLColor.FromHtml("#52A36A");
        for (var index = 0; index < columns.Count; index++)
            worksheet.Column(index + 1).Width = columns[index].Field switch
            {
                "PointTag" => 34,
                "Address" => 24,
                "DataType" => 18,
                "ByteOrder" or "WordOrder" => 20,
                "Access" => 18,
                "RawMin" or "RawMax" or "EngMin" or "EngMax" => 14,
                "Description" => 36,
                _ => 18
            };

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

        var targets = request?.Targets is { Count: > 0 }
            ? request.Targets
            : new[]
            {
                new DevicePointTemplateTarget(
                    string.Empty,
                    "示例设备",
                    "示例设备",
                    DevicePointProtocol.Simulation,
                    "仿真",
                    new HashSet<DevicePointDataType>
                    {
                        DevicePointDataType.Char,
                        DevicePointDataType.Byte,
                        DevicePointDataType.Int16,
                        DevicePointDataType.UInt16,
                        DevicePointDataType.Int32,
                        DevicePointDataType.UInt32,
                        DevicePointDataType.Float32,
                        DevicePointDataType.Double
                    })
            };
        var rows = new List<string[]>();
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            var deviceCode = string.IsNullOrWhiteSpace(target.DeviceCode) ? "示例设备" : target.DeviceCode;
            var groupCode = !string.IsNullOrWhiteSpace(request?.DefaultGroupCode)
                ? request!.DefaultGroupCode!
                : request?.GroupCodes
                    .FirstOrDefault(code => !string.Equals(code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
                    ?? string.Empty;
            var includeDevicePrefix = targets.Count > 1;
            var firstTag = DevicePointTag.Format(
                includeDevicePrefix ? deviceCode : string.Empty,
                groupCode,
                "试验压力");
            var commandTag = DevicePointTag.Format(
                includeDevicePrefix ? deviceCode : string.Empty,
                groupCode,
                "启动命令");
            rows.Add(CreateExampleRow(
                firstTag,
                GetAddressExample(target, isCommand: false),
                GetRawDataExample(target, isCommand: false),
                GetByteOrderExample(target),
                GetWordOrderExample(target, isCommand: false),
                "0",
                "10000",
                "0",
                "10",
                "采集压力示例"));
            rows.Add(CreateExampleRow(
                commandTag,
                GetAddressExample(target, isCommand: true),
                GetRawDataExample(target, isCommand: true),
                GetByteOrderExample(target),
                GetWordOrderExample(target, isCommand: true),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                "高风险写入示例",
                access: "读写"));
        }
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value = rows[rowIndex][columnIndex];
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1FAF3");
            }
        }

        var noteRow = rows.Count + 4;
        worksheet.Range(noteRow, 1, noteRow, columns.Count).Merge();
        var targetDescription = request is null || request.Targets.Count == 0
            ? "通用示例仅用于理解填写方式。"
            : $"本页示例按“{request.ScopeDisplayName}”生成，通信方式为：{string.Join("、", request.Targets.Select(target => target.ProtocolDisplayName).Distinct())}。";
        worksheet.Cell(noteRow, 1).Value = $"说明：{targetDescription}请将真实点位填写到“点位模板”工作表后再导入；示例中的地址仅用于说明格式。";
        worksheet.Cell(noteRow, 1).Style.Font.FontColor = XLColor.FromHtml("#7A5B00");
        worksheet.Cell(noteRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF9EB");
        worksheet.Cell(noteRow, 1).Style.Alignment.WrapText = true;
        worksheet.Row(noteRow).Height = 34;
        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, 1, columns.Count).SetAutoFilter();
    }

    private static string[] CreateExampleRow(
        string pointTag,
        string address,
        string rawDataType,
        string byteOrder,
        string wordOrder,
        string rawMin,
        string rawMax,
        string engMin,
        string engMax,
        string description,
        string access = "只读")
        => new[]
        {
            pointTag,
            address,
            rawDataType,
            byteOrder,
            wordOrder,
            access,
            rawMin,
            rawMax,
            engMin,
            engMax,
            description
        };

    private static string GetAddressExample(
        DevicePointTemplateTarget target,
        bool isCommand)
    {
        if (target.Protocol == DevicePointProtocol.SiemensS7
            && target.ProfileText.Contains("200", StringComparison.OrdinalIgnoreCase)
            && !target.ProfileText.Contains("1200", StringComparison.OrdinalIgnoreCase))
            return isCommand ? "M10.1" : "VW5022";

        return target.Protocol switch
        {
            DevicePointProtocol.Simulation => isCommand ? "sim.start" : "sim.pressure",
            DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp => isCommand ? "C:1" : "HR:0",
            DevicePointProtocol.SiemensS7 => isCommand ? "DB143.DBX34.4" : "DB144.DBD88",
            _ => isCommand ? "sim.start" : "sim.pressure"
        };
    }

    private static string GetRawDataExample(
        DevicePointTemplateTarget target,
        bool isCommand)
    {
        if (target.Protocol is DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp)
        {
            var modbusPreferred = isCommand
                ? new[] { DevicePointDataType.Bool, DevicePointDataType.UInt16, DevicePointDataType.Float32 }
                : new[] { DevicePointDataType.Float32, DevicePointDataType.Double, DevicePointDataType.Int16 };
            var modbusSelected = modbusPreferred.FirstOrDefault(type => target.SupportedDataTypes.Contains(type));
            if (modbusSelected == default && target.SupportedDataTypes.Count > 0)
                modbusSelected = target.SupportedDataTypes.First();
            return DevicePointTypeCatalog.ToDisplayName(
                modbusSelected == default ? DevicePointDataType.Int16 : modbusSelected);
        }

        var preferred = isCommand
            ? new[] { DevicePointDataType.Byte, DevicePointDataType.Char, DevicePointDataType.UInt16 }
            : new[] { DevicePointDataType.Float32, DevicePointDataType.Double, DevicePointDataType.Int16 };
        var selected = preferred.FirstOrDefault(type => target.SupportedDataTypes.Contains(type));
        if (selected == default && target.SupportedDataTypes.Count > 0)
            selected = target.SupportedDataTypes.First();
        return DevicePointTypeCatalog.ToDisplayName(selected == default ? DevicePointDataType.Decimal : selected);
    }

    private static string GetByteOrderExample(DevicePointTemplateTarget target)
        => target.Protocol is DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp
            ? "大端"
            : string.Empty;

    private static string GetWordOrderExample(DevicePointTemplateTarget target, bool isCommand)
    {
        if (target.Protocol is not (DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp))
            return string.Empty;

        var typeText = GetRawDataExample(target, isCommand);
        return DevicePointTemplateDefinition.TryMapDataType(typeText, out var type)
            && type is (DevicePointDataType.Int32
                or DevicePointDataType.UInt32
                or DevicePointDataType.Float32
                or DevicePointDataType.Double)
            ? "高字在前"
            : "无";
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
