using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using XXX.TestBench.Core.Common;
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
            var maxColumns = DevicePointTemplateDefinition.Columns.Count;
            if (rows[i].Length > maxColumns)
            {
                issues.Add(new DevicePointImportIssue(rowNumber,
                    $"数据列超过模板固定列数 {maxColumns} 列"));
                continue;
            }
            if (TryParseSimplifiedEntry(rows[i], headerMap, out var entry, out var error))
                entries.Add((rowNumber, entry));
            else
                issues.Add(new DevicePointImportIssue(rowNumber, error));
        }

        foreach (var group in entries.GroupBy(item => item.Entry.Code, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var item in group)
                issues.Add(new DevicePointImportIssue(item.RowNumber, $"点位编码重复：{group.Key}"));

        var addressGroups = entries.GroupBy(item =>
            $"{item.Entry.DeviceCode}\u001f{item.Entry.AddressDefinition?.ToCanonical(item.Entry.Address) ?? item.Entry.Address}",
            StringComparer.OrdinalIgnoreCase);
        foreach (var group in addressGroups.Where(group => group.Count() > 1))
            foreach (var item in group)
                issues.Add(new DevicePointImportIssue(item.RowNumber,
                    "同一设备的地址参数重复"));

        var parsed = entries.Select(item => item.Entry).ToList();
        if (parsed.Count == 0 && issues.Count == 0)
            issues.Add(new DevicePointImportIssue(0, "没有可导入的点位数据"));

        if (parsed.Count > 0 && issues.Count == 0)
        {
            try
            {
                ValidateImportedV3(parsed);
            }
            catch (Core.Configuration.ConfigValidationException ex)
            {
                issues.Add(new DevicePointImportIssue(0, ex.Message));
            }
        }

        return new DevicePointImportResult(
            entries.Select(item => new DevicePointImportRow(item.RowNumber, item.Entry)).ToList(),
            issues.OrderBy(issue => issue.RowNumber).ToList());
    }

    /// <summary>
    /// 严格读取模板表头：列名必须为中文、数量和顺序必须完全一致。
    /// </summary>
    private static bool TryReadTemplateHeader(
        IReadOnlyList<string> header,
        out IReadOnlyDictionary<string, int> headerMap,
        out string error)
    {
        var columns = DevicePointTemplateDefinition.Columns;
        if (header.Count != columns.Count)
        {
            headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            error = $"模板列数量不正确：当前 v{DevicePointTemplateDefinition.CurrentTemplateVersion} 必须为 {columns.Count} 列，实际 {header.Count} 列。请下载并使用“{DevicePointTemplateDefinition.DefaultFileName}”，不要修改列名或顺序";
            return false;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < columns.Count; index++)
        {
            var actual = CleanHeader(header[index]);
            var column = columns[index];
            if (!string.Equals(actual, column.Header, StringComparison.Ordinal))
            {
                headerMap = map;
                error = $"模板第1行第 {index + 1} 列应为“{column.Header}”，实际为“{(string.IsNullOrEmpty(actual) ? "未填写" : actual)}”。请下载并使用“{DevicePointTemplateDefinition.DefaultFileName}”，不要修改列名或顺序";
                return false;
            }

            map[column.Field] = index;
        }

        headerMap = map;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 解析当前简化中文模板。设备和分组不再重复填写，身份由下载范围或点位标签前缀确定。
    /// </summary>
    private static bool TryParseSimplifiedEntry(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headerMap,
        out PointsConfig.PointEntry entry,
        out string error)
    {
        var errors = new List<string>();
        var tagText = Get(row, headerMap, "PointTag");
        var address = Get(row, headerMap, "Address");
        var dataTypeText = Get(row, headerMap, "DataType");
        var accessText = Get(row, headerMap, "Access");

        if (string.IsNullOrWhiteSpace(tagText))
            errors.Add("点位标签不能为空");
        if (string.IsNullOrWhiteSpace(address))
            errors.Add("地址不能为空");
        if (string.IsNullOrWhiteSpace(dataTypeText))
            errors.Add("数据类型不能为空");

        DevicePointTag.ParsedTag? tag = null;
        if (!string.IsNullOrWhiteSpace(tagText)
            && !DevicePointTag.TryParse(tagText, out tag!, out var tagError))
            errors.Add(tagError);

        var dataType = ReadDataType(dataTypeText, errors);
        _ = TryCreateModbusAddressDefinition(address, out var modbusAddressDefinition);
        var (isWritable, risk) = ReadAccess(accessText, errors);
        var rawMin = ReadDecimal(Get(row, headerMap, "RawMin"), "原始下限", errors);
        var rawMax = ReadDecimal(Get(row, headerMap, "RawMax"), "原始上限", errors);
        var engMin = ReadDecimal(Get(row, headerMap, "EngMin"), "工程下限", errors);
        var engMax = ReadDecimal(Get(row, headerMap, "EngMax"), "工程上限", errors);
        if (rawMin.HasValue != rawMax.HasValue || engMin.HasValue != engMax.HasValue || rawMin.HasValue != engMin.HasValue)
            errors.Add("量程必须完整填写原始/工程上下限，或全部留空");
        if (rawMin > rawMax || engMin > engMax)
            errors.Add("量程下限不能大于上限");

        if (errors.Count > 0 || tag is null)
        {
            entry = null!;
            error = string.Join("；", errors);
            return false;
        }

        var directAddress = address.Trim();
        var normalizedAddress = modbusAddressDefinition is { Area: { Length: > 0 }, Offset: { } offset }
            ? $"{modbusAddressDefinition.Area}:{offset}"
            : directAddress;
        entry = new PointsConfig.PointEntry
        {
            // 有设备前缀时保留完整标签，确保跨设备同名点位仍可区分；
            // 单设备标签则使用本地“分组.点位”作为稳定点位编码。
            Code = tag.FullTag,
            Name = tag.PointName,
            DeviceCode = tag.DeviceCode ?? string.Empty,
            GroupCode = tag.GroupCode ?? string.Empty,
            Address = normalizedAddress,
            OriginalAddress = directAddress,
            CanonicalAddress = normalizedAddress,
            // Modbus 地址在导入边界就保存区域/偏移结构，保证导入点位和界面新建点位
            // 使用同一套规范地址键；其他协议继续保留原始逻辑地址。
            AddressDefinition = modbusAddressDefinition
                ?? new PointAddressDefinition { LogicalAddress = directAddress },
            Protocol = directAddress.StartsWith("sim.", StringComparison.OrdinalIgnoreCase)
                ? DevicePointTypeCatalog.ToStorage(DevicePointProtocol.Simulation)
                : string.Empty,
            DataType = DevicePointTypeCatalog.ToStorage(dataType),
            // S7 解码参数和 Modbus 设备级数据排列都不由点位导入模板覆盖。
            S7DecodeOptions = null,
            RawMin = rawMin,
            RawMax = rawMax,
            EngMin = engMin,
            EngMax = engMax,
            IsWritable = isWritable,
            RiskLevel = risk,
            Description = Get(row, headerMap, "Description")
        };
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 在基础层识别 Modbus 的规范地址，只为导入身份和最终驱动校验服务；
    /// 最终区域、范围和类型规则仍由已注册的 Modbus 驱动描述器负责。
    /// </summary>
    private static bool TryCreateModbusAddressDefinition(
        string value,
        out PointAddressDefinition? definition)
    {
        definition = null;
        var text = value.Trim();
        var separator = text.IndexOf(':');
        if (separator > 0
            && int.TryParse(text[(separator + 1)..].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var offset)
            && offset is >= 0 and <= ushort.MaxValue)
        {
            var area = text[..separator].Trim().ToUpperInvariant() switch
            {
                "C" or "COIL" or "COILS" => "C",
                "DI" or "DISCRETEINPUT" or "DISCRETEINPUTS" => "DI",
                "HR" or "HOLDINGREGISTER" or "HOLDINGREGISTERS" => "HR",
                "IR" or "INPUTREGISTER" or "INPUTREGISTERS" => "IR",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(area))
            {
                definition = new PointAddressDefinition { Area = area, Offset = offset };
                return true;
            }
        }

        if (text.Length == 5
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var reference))
        {
            var area = text[0] switch
            {
                '0' => "C",
                '1' => "DI",
                '3' => "IR",
                '4' => "HR",
                _ => string.Empty
            };
            var legacyOffset = reference % 10000 - 1;
            if (!string.IsNullOrWhiteSpace(area) && legacyOffset is >= 0 and <= ushort.MaxValue)
            {
                definition = new PointAddressDefinition { Area = area, Offset = legacyOffset };
                return true;
            }
        }

        return false;
    }

    private static DevicePointDataType ReadDataType(string value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return DevicePointDataType.Unknown;
        if (DevicePointTemplateDefinition.TryMapDataType(value, out var dataType))
            return dataType;
        errors.Add($"原始数据类型不受支持：{value}（请从模板下拉框选择）");
        return DevicePointDataType.Unknown;
    }

    private static void ValidateImportedV3(IReadOnlyList<PointsConfig.PointEntry> points)
    {
        var groups = new List<PointsConfig.PointGroupEntry>();
        var groupIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            var deviceId = StableIdentity.FromSeed($"import-device|{point.DeviceCode.Trim()}");
            var groupCode = string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode.Trim();
            var groupKey = $"{deviceId}\u001f{groupCode}";
            if (groupIds.ContainsKey(groupKey)) continue;
            var groupId = StableIdentity.FromSeed($"import-group|{deviceId}|{groupCode}");
            groupIds[groupKey] = groupId;
            groups.Add(new PointsConfig.PointGroupEntry
            {
                Id = groupId,
                DeviceId = deviceId,
                Code = groupCode,
                Name = groupCode == "DEFAULT" ? "未分组" : groupCode,
                SortOrder = groups.Count
            });
        }

        var config = new PointsConfig { SchemaVersion = PointsConfig.CurrentSchemaVersion, Groups = groups };
        foreach (var point in points)
        {
            var deviceId = StableIdentity.FromSeed($"import-device|{point.DeviceCode.Trim()}");
            var groupCode = string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode.Trim();
            var clone = new PointsConfig.PointEntry
            {
                Id = string.IsNullOrWhiteSpace(point.Id)
                    ? StableIdentity.FromSeed($"import-point|{point.DeviceCode}|{point.Code}|{point.Address}")
                    : point.Id,
                Code = point.Code,
                Name = point.Name,
                DeviceId = deviceId,
                DeviceCode = point.DeviceCode,
                GroupId = groupIds[$"{deviceId}\u001f{groupCode}"],
                GroupCode = groupCode,
                Address = point.Address,
                AddressDefinition = point.AddressDefinition,
                Protocol = point.Protocol,
                DataType = point.DataType,
                S7DecodeOptions = point.S7DecodeOptions,
                WritePolicy = point.WritePolicy,
                RawMin = point.RawMin,
                RawMax = point.RawMax,
                EngMin = point.EngMin,
                EngMax = point.EngMax,
                IsWritable = point.IsWritable,
                RiskLevel = point.RiskLevel,
                Description = point.Description
            };
            config.Points.Add(clone);
        }
        config.Validate();
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
    /// 解析数字，允许常见区域设置的小数点写法。
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
    /// 把 KEPServer 的 Client Access 和中文访问权限收敛为一个客户字段。
    /// </summary>
    private static (bool IsWritable, WriteRiskLevel Risk) ReadAccess(
        string value,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (false, WriteRiskLevel.Normal);

        var normalized = value.Trim()
            .Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "只读" or "r" or "readonly" or "read-only" or "读" =>
                (false, WriteRiskLevel.Normal),
            "读写" or "可写" or "可写(普通)" or "可写(一般)" or "rw" or "r/w"
                or "readwrite" or "read-write" =>
                (true, WriteRiskLevel.Normal),
            "可写(高风险)" or "高风险" or "高风险可写" =>
                (true, WriteRiskLevel.HighRisk),
            _ => ReadInvalidAccess(value, errors)
        };
    }

    private static (bool IsWritable, WriteRiskLevel Risk) ReadInvalidAccess(
        string value,
        ICollection<string> errors)
    {
        errors.Add($"访问权限不受支持：{value}（请填写 只读或读写）");
        return (false, WriteRiskLevel.Normal);
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
        => new(Array.Empty<DevicePointImportRow>(), new[] { new DevicePointImportIssue(0, message) });

    /// <summary>
    /// 清理表头首尾空格和 UTF-8 BOM；不转换列名，也不接受英文别名。
    /// </summary>
    private static string CleanHeader(string value)
        => (value ?? string.Empty).Trim().Trim('\uFEFF');
}
