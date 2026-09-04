using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 设备点位 Excel/CSV 导入器：只接受设备点位导入模板的固定中文表头，输出统一的声明式点位数据。
/// </summary>
public sealed class DevicePointImporter : IDevicePointImporter
{
    /// <summary>
    /// 后台导入点位文件并返回解析结果。
    /// </summary>
    public Task<DevicePointImportResult> ImportAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => ImportCore(filePath, ct), ct);

    /// <summary>
    /// 校验文件并调用对应读取器解析。
    /// </summary>
    private static DevicePointImportResult ImportCore(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Invalid("未选择导入文件");
        if (!File.Exists(filePath))
            return Invalid($"文件不存在：{filePath}");

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension is not ".xlsx" and not ".csv")
            return Invalid("仅支持 .xlsx 或 .csv 文件（旧版 .xls 请另存为 .xlsx）");

        try
        {
            var rows = extension == ".xlsx" ? ReadExcel(filePath) : ReadCsv(filePath);
            return ParseRows(rows, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Invalid($"读取点位文件失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            return Invalid($"解析点位文件失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 解析表头与数据行，逐行收集问题。
    /// </summary>
    private static DevicePointImportResult ParseRows(IReadOnlyList<string[]> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return Invalid("点位文件为空");

        if (!TryReadTemplateHeader(rows[0], out var headerMap, out var headerError))
            return Invalid(headerError);

        var entries = new List<(int RowNumber, PointsConfig.PointEntry Entry)>();
        var issues = new List<DevicePointImportIssue>();
        for (var i = 1; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (rows[i].All(string.IsNullOrWhiteSpace)) continue;

            var rowNumber = i + 1;
            if (rows[i].Length > DevicePointTemplateDefinition.Columns.Count)
            {
                issues.Add(new DevicePointImportIssue(rowNumber,
                    $"数据列超过模板固定列数 {DevicePointTemplateDefinition.Columns.Count} 列"));
                continue;
            }
            if (TryParseEntry(rows[i], headerMap, out var entry, out var error))
                entries.Add((rowNumber, entry));
            else
                issues.Add(new DevicePointImportIssue(rowNumber, error));
        }

        foreach (var group in entries.GroupBy(item => item.Entry.Code, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var item in group)
                issues.Add(new DevicePointImportIssue(item.RowNumber, $"点位编码重复：{group.Key}"));

        foreach (var group in entries.GroupBy(item => $"{item.Entry.Protocol}\u001f{item.Entry.Address}", StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var item in group)
                issues.Add(new DevicePointImportIssue(item.RowNumber, "协议/地址组合重复"));

        var parsed = entries.Select(item => item.Entry).ToList();
        if (parsed.Count == 0 && issues.Count == 0)
            issues.Add(new DevicePointImportIssue(0, "没有可导入的点位数据"));

        if (parsed.Count > 0 && issues.Count == 0)
        {
            try
            {
                var config = new PointsConfig { SchemaVersion = PointsConfig.CurrentSchemaVersion };
                config.Points.AddRange(parsed);
                config.Validate();
            }
            catch (Core.Configuration.ConfigValidationException ex)
            {
                issues.Add(new DevicePointImportIssue(0, ex.Message));
            }
        }

        return new DevicePointImportResult(parsed, issues.OrderBy(issue => issue.RowNumber).ToList());
    }

    /// <summary>
    /// 严格读取模板表头：列名必须为中文、数量和顺序必须完全一致。
    /// </summary>
    private static bool TryReadTemplateHeader(
        IReadOnlyList<string> header,
        out IReadOnlyDictionary<string, int> headerMap,
        out string error)
    {
        var expected = DevicePointTemplateDefinition.Columns;
        if (header.Count != expected.Count)
        {
            headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            error = $"模板列数量不正确：第1行应有 {expected.Count} 列，实际 {header.Count} 列。请下载并使用“{DevicePointTemplateDefinition.DefaultFileName}”，不要修改列名或顺序";
            return false;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < expected.Count; index++)
        {
            var actual = CleanHeader(header[index]);
            if (!string.Equals(actual, expected[index].Header, StringComparison.Ordinal))
            {
                headerMap = map;
                error = $"模板第1行第 {index + 1} 列应为“{expected[index].Header}”，实际为“{(string.IsNullOrEmpty(actual) ? "未填写" : actual)}”。请下载并使用“{DevicePointTemplateDefinition.DefaultFileName}”，不要修改列名或顺序";
                return false;
            }

            map[expected[index].Field] = index;
        }

        headerMap = map;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 解析单行并校验必填项与量程。
    /// </summary>
    private static bool TryParseEntry(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headerMap,
        out PointsConfig.PointEntry entry,
        out string error)
    {
        var errors = new List<string>();
        var code = Get(row, headerMap, "Code");
        var name = Get(row, headerMap, "Name");
        var protocol = Get(row, headerMap, "Protocol");
        var address = Get(row, headerMap, "Address");
        var dataType = Get(row, headerMap, "DataType");

        if (string.IsNullOrWhiteSpace(code)) errors.Add("点位编码不能为空");
        if (string.IsNullOrWhiteSpace(protocol)) errors.Add("协议不能为空");
        if (string.IsNullOrWhiteSpace(address)) errors.Add("地址不能为空");
        if (string.IsNullOrWhiteSpace(dataType)) errors.Add("数据类型不能为空");

        var rawMin = ReadDecimal(Get(row, headerMap, "RawMin"), "原始下限", errors);
        var rawMax = ReadDecimal(Get(row, headerMap, "RawMax"), "原始上限", errors);
        var engMin = ReadDecimal(Get(row, headerMap, "EngMin"), "工程下限", errors);
        var engMax = ReadDecimal(Get(row, headerMap, "EngMax"), "工程上限", errors);
        if (rawMin.HasValue != rawMax.HasValue || engMin.HasValue != engMax.HasValue || rawMin.HasValue != engMin.HasValue)
            errors.Add("量程必须完整填写原始/工程上下限，或全部留空");
        if (rawMin > rawMax || engMin > engMax)
            errors.Add("量程下限不能大于上限");

        var isWritable = ReadBoolean(Get(row, headerMap, "IsWritable"), "可写", false, errors);
        var isEnabled = ReadBoolean(Get(row, headerMap, "IsEnabled"), "启用", true, errors);
        var risk = ReadRisk(Get(row, headerMap, "RiskLevel"), errors);

        if (errors.Count > 0)
        {
            entry = null!;
            error = string.Join("；", errors);
            return false;
        }

        entry = new PointsConfig.PointEntry
        {
            Code = code.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim(),
            Protocol = protocol.Trim(),
            Address = address.Trim(),
            DataType = dataType.Trim(),
            Unit = Get(row, headerMap, "Unit"),
            RawMin = rawMin,
            RawMax = rawMax,
            EngMin = engMin,
            EngMax = engMax,
            IsWritable = isWritable,
            IsEnabled = isEnabled,
            RiskLevel = risk,
            Description = Get(row, headerMap, "Description")
        };
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 按列名读取单元格文本并去除空白。
    /// </summary>
    private static string Get(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> headerMap, string field)
    {
        if (!headerMap.TryGetValue(field, out var index) || index >= row.Count) return string.Empty;
        return row[index].Trim().Trim('\uFEFF');
    }

    /// <summary>
    /// 解析数字，兼容不同区域的小数点写法。
    /// </summary>
    private static decimal? ReadDecimal(string value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)) return invariant;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)) return current;
        errors.Add($"{label}不是有效数字：{value}");
        return null;
    }

    /// <summary>
    /// 解析布尔值，支持中文与常用英文写法。
    /// </summary>
    private static bool ReadBoolean(string value, string label, bool defaultValue, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        switch (value.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "y":
            case "是":
            case "启用":
            case "可写":
                return true;
            case "0":
            case "false":
            case "no":
            case "n":
            case "否":
            case "停用":
            case "不可写":
                return false;
            default:
                errors.Add($"{label}不是有效布尔值：{value}（支持 是/否、1/0、true/false）");
                return defaultValue;
        }
    }

    /// <summary>
    /// 解析写入风险等级。
    /// </summary>
    private static WriteRiskLevel ReadRisk(string value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return WriteRiskLevel.Normal;
        switch (value.Trim().ToLowerInvariant())
        {
            case "0":
            case "normal":
            case "普通":
            case "一般":
                return WriteRiskLevel.Normal;
            case "1":
            case "highrisk":
            case "high-risk":
            case "高风险":
                return WriteRiskLevel.HighRisk;
            default:
                errors.Add($"风险等级不受支持：{value}（支持 Normal/HighRisk）");
                return WriteRiskLevel.Normal;
        }
    }

    /// <summary>
    /// 读取名称固定的第一个工作表为二维文本表。
    /// </summary>
    private static IReadOnlyList<string[]> ReadExcel(string path)
    {
        using var workbook = new XLWorkbook(path);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("Excel 中没有工作表");
        if (!string.Equals(worksheet.Name, DevicePointTemplateDefinition.DataSheetName, StringComparison.Ordinal))
            throw new InvalidDataException($"Excel 首个工作表必须命名为“{DevicePointTemplateDefinition.DataSheetName}”");
        var firstRow = worksheet.FirstRowUsed();
        var lastRow = worksheet.LastRowUsed();
        var firstColumn = worksheet.FirstColumnUsed();
        var lastColumn = worksheet.LastColumnUsed();
        if (firstRow is null || lastRow is null || firstColumn is null || lastColumn is null)
            return Array.Empty<string[]>();

        var rows = new List<string[]>();
        for (var rowNumber = firstRow.RowNumber(); rowNumber <= lastRow.RowNumber(); rowNumber++)
        {
            var values = new string[lastColumn.ColumnNumber() - firstColumn.ColumnNumber() + 1];
            for (var columnNumber = firstColumn.ColumnNumber(); columnNumber <= lastColumn.ColumnNumber(); columnNumber++)
                values[columnNumber - firstColumn.ColumnNumber()] = worksheet.Cell(rowNumber, columnNumber).GetFormattedString();
            rows.Add(values);
        }
        return rows;
    }

    /// <summary>
    /// 读取文本文件；编码无效时按简体中文代码页重读。
    /// </summary>
    private static IReadOnlyList<string[]> ReadCsv(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = File.ReadAllText(path, Encoding.GetEncoding(936));
        }
        return ParseCsv(text);
    }

    /// <summary>
    /// 手工解析带引号与换行的文本。
    /// </summary>
    private static IReadOnlyList<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            if (ch == '"') quoted = true;
            else if (ch == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                if (fields.Any(value => !string.IsNullOrWhiteSpace(value)))
                    rows.Add(fields.ToArray());
                fields.Clear();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else
            {
                field.Append(ch);
            }
        }

        if (quoted) throw new InvalidDataException("CSV 存在未闭合的引号");
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (fields.Any(value => !string.IsNullOrWhiteSpace(value)))
                rows.Add(fields.ToArray());
        }
        return rows;
    }

    /// <summary>
    /// 生成包含整体错误的结果。
    /// </summary>
    private static DevicePointImportResult Invalid(string message)
        => new(Array.Empty<PointsConfig.PointEntry>(), new[] { new DevicePointImportIssue(0, message) });

    /// <summary>
    /// 清理表头首尾空格和 UTF-8 BOM；不转换列名、不兼容英文别名。
    /// </summary>
    private static string CleanHeader(string value)
        => (value ?? string.Empty).Trim().Trim('\uFEFF');
}
