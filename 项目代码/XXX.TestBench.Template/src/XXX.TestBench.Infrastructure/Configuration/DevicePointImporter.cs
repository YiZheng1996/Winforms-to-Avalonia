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

        if (!TryReadTemplateHeader(rows[0], out var headerMap, out var headerError, out var legacyHeader))
            return Invalid(headerError);

        var entries = new List<(int RowNumber, PointsConfig.PointEntry Entry)>();
        var issues = new List<DevicePointImportIssue>();
        for (var i = 1; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (rows[i].All(string.IsNullOrWhiteSpace)) continue;

            var rowNumber = i + 1;
            var maxColumns = legacyHeader
                ? DevicePointTemplateDefinition.LegacyColumns.Count
                : DevicePointTemplateDefinition.Columns.Count;
            if (rows[i].Length > maxColumns)
            {
                issues.Add(new DevicePointImportIssue(rowNumber,
                    $"数据列超过模板固定列数 {maxColumns} 列"));
                continue;
            }
            if (TryParseEntry(rows[i], headerMap, legacyHeader, out var entry, out var error))
                entries.Add((rowNumber, entry));
            else
                issues.Add(new DevicePointImportIssue(rowNumber, error));
        }

        foreach (var group in entries.GroupBy(item => item.Entry.Code, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var item in group)
                issues.Add(new DevicePointImportIssue(item.RowNumber, $"点位编码重复：{group.Key}"));

        var legacyRows = legacyHeader || (entries.Count > 0 && entries.All(item => string.IsNullOrWhiteSpace(item.Entry.DeviceCode)));
        var addressGroups = legacyRows
            ? entries.GroupBy(item => $"{item.Entry.ProtocolKind}\u001f{item.Entry.Address}", StringComparer.OrdinalIgnoreCase)
            : entries.GroupBy(item => $"{item.Entry.DeviceCode}\u001f{item.Entry.AddressDefinition?.ToCanonical(item.Entry.Address) ?? item.Entry.Address}", StringComparer.OrdinalIgnoreCase);
        foreach (var group in addressGroups.Where(group => group.Count() > 1))
            foreach (var item in group)
                issues.Add(new DevicePointImportIssue(item.RowNumber,
                    legacyRows ? "通信方式/设备地址组合重复" : "同一设备的地址参数重复"));

        var parsed = entries.Select(item => item.Entry).ToList();
        if (parsed.Count == 0 && issues.Count == 0)
            issues.Add(new DevicePointImportIssue(0, "没有可导入的点位数据"));

        if (parsed.Count > 0 && issues.Count == 0)
        {
            try
            {
                if (legacyRows)
                {
                    var config = new PointsConfig { SchemaVersion = PointsConfig.LegacySchemaVersion };
                    config.Points.AddRange(parsed);
                    config.Validate();
                }
                else
                {
                    ValidateImportedV3(parsed);
                }
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
        out string error,
        out bool legacyHeader)
    {
        var expected = DevicePointTemplateDefinition.Columns;
        legacyHeader = false;
        if (header.Count == DevicePointTemplateDefinition.LegacyColumns.Count)
        {
            var legacyMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < DevicePointTemplateDefinition.LegacyColumns.Count; index++)
            {
                var actual = CleanHeader(header[index]);
                var legacyColumn = DevicePointTemplateDefinition.LegacyColumns[index];
                if (!string.Equals(actual, legacyColumn.Header, StringComparison.Ordinal))
                {
                    headerMap = legacyMap;
                    error = $"模板第1行第 {index + 1} 列应为“{legacyColumn.Header}”，实际为“{(string.IsNullOrEmpty(actual) ? "未填写" : actual)}”。请下载并使用“{DevicePointTemplateDefinition.DefaultFileName}”，不要修改列名或顺序";
                    return false;
                }

                legacyMap[legacyColumn.Field] = index;
            }

            headerMap = legacyMap;
            legacyHeader = true;
            error = string.Empty;
            return true;
        }

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
        bool legacyHeader,
        out PointsConfig.PointEntry entry,
        out string error)
    {
        if (legacyHeader)
            return TryParseLegacyEntry(row, headerMap, out entry, out error);

        return TryParseV2Entry(row, headerMap, out entry, out error);
    }

    /// <summary>
    /// 解析旧版单设备 14 列行。该分支只用于兼容，不参与新模板导出。
    /// </summary>
    private static bool TryParseLegacyEntry(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headerMap,
        out PointsConfig.PointEntry entry,
        out string error)
    {
        var errors = new List<string>();
        var code = Get(row, headerMap, "Code");
        var name = Get(row, headerMap, "Name");
        var protocolText = Get(row, headerMap, "Protocol");
        var address = Get(row, headerMap, "Address");
        var dataTypeText = Get(row, headerMap, "DataType");

        if (string.IsNullOrWhiteSpace(code)) errors.Add("点位编码不能为空");
        if (string.IsNullOrWhiteSpace(protocolText)) errors.Add("通信方式不能为空");
        if (string.IsNullOrWhiteSpace(address)) errors.Add("地址不能为空");
        if (string.IsNullOrWhiteSpace(dataTypeText)) errors.Add("数据类型不能为空");

        var protocol = ReadProtocol(protocolText, errors);
        var dataType = ReadDataType(dataTypeText, errors);

        var rawMin = ReadDecimal(Get(row, headerMap, "RawMin"), "原始下限", errors);
        var rawMax = ReadDecimal(Get(row, headerMap, "RawMax"), "原始上限", errors);
        var engMin = ReadDecimal(Get(row, headerMap, "EngMin"), "工程下限", errors);
        var engMax = ReadDecimal(Get(row, headerMap, "EngMax"), "工程上限", errors);
        if (rawMin.HasValue != rawMax.HasValue || engMin.HasValue != engMax.HasValue || rawMin.HasValue != engMin.HasValue)
            errors.Add("量程必须完整填写原始/工程上下限，或全部留空");
        if (rawMin > rawMax || engMin > engMax)
            errors.Add("量程下限不能大于上限");

        var isWritable = ReadBoolean(Get(row, headerMap, "IsWritable"), "是否允许写入", false, errors);
        var isEnabled = ReadBoolean(Get(row, headerMap, "IsEnabled"), "是否启用", true, errors);
        var risk = ReadRisk(Get(row, headerMap, "RiskLevel"), errors);
        if (!isWritable && risk == WriteRiskLevel.HighRisk)
            errors.Add("是否允许写入=否时，写入风险必须填写“普通”");

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
            Protocol = DevicePointTypeCatalog.ToStorage(protocol),
            Address = address.Trim(),
            DataType = DevicePointTypeCatalog.ToStorage(dataType),
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
    /// 解析当前 19 列行。设备编码和分组编码保留在导入边界，稍后由完整配置解析为稳定身份。
    /// </summary>
    private static bool TryParseV2Entry(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headerMap,
        out PointsConfig.PointEntry entry,
        out string error)
    {
        var errors = new List<string>();
        var pointId = Get(row, headerMap, "PointId");
        var code = Get(row, headerMap, "Code");
        var name = Get(row, headerMap, "Name");
        var deviceCode = Get(row, headerMap, "DeviceCode");
        var groupCode = Get(row, headerMap, "GroupCode");
        var addressTypeText = Get(row, headerMap, "AddressType");
        var addressParameters = Get(row, headerMap, "AddressParameters");
        var rawDataTypeText = Get(row, headerMap, "RawDataType");

        if (!string.IsNullOrWhiteSpace(pointId) && !Guid.TryParse(pointId, out _))
            errors.Add($"点位标识不是有效 GUID：{pointId}");
        if (string.IsNullOrWhiteSpace(code)) errors.Add("点位编码不能为空");
        if (string.IsNullOrWhiteSpace(deviceCode)) errors.Add("设备编码不能为空");
        if (string.IsNullOrWhiteSpace(addressTypeText)) errors.Add("地址类型不能为空");
        if (string.IsNullOrWhiteSpace(addressParameters)) errors.Add("地址参数不能为空");
        if (string.IsNullOrWhiteSpace(rawDataTypeText)) errors.Add("原始数据类型不能为空");

        var address = ReadAddress(addressTypeText, addressParameters, errors);
        var rawDataType = ReadRawDataType(rawDataTypeText, errors);
        var byteOrder = ReadByteOrder(Get(row, headerMap, "ByteOrder"), errors);
        var wordOrder = ReadWordOrder(Get(row, headerMap, "WordOrder"), errors);

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
        if (!isWritable && risk == WriteRiskLevel.HighRisk)
            errors.Add("可写=否时，风险等级必须填写“普通”");

        if (errors.Count > 0)
        {
            entry = null!;
            error = string.Join("；", errors);
            return false;
        }

        entry = new PointsConfig.PointEntry
        {
            Id = pointId,
            Code = code.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim(),
            DeviceCode = deviceCode.Trim(),
            GroupCode = groupCode.Trim(),
            Protocol = address!.Area == "Simulation" ? DevicePointTypeCatalog.ToStorage(DevicePointProtocol.Simulation) : string.Empty,
            Address = addressParameters.Trim(),
            AddressDefinition = address,
            DataType = DevicePointTypeCatalog.ToStorage(rawDataType),
            RawDataType = DevicePointTypeCatalog.ToStorage(rawDataType),
            DecodeOptions = new DecodeOptions { ByteOrder = byteOrder, WordOrder = wordOrder },
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

    private static PointAddressDefinition? ReadAddress(string addressType, string parameters, ICollection<string> errors)
    {
        if (!DevicePointTemplateDefinition.TryMapAddressType(addressType, out var area))
        {
            errors.Add($"地址类型不受支持：{addressType}（请从模板下拉框选择）");
            return null;
        }

        if (string.IsNullOrWhiteSpace(parameters)) return null;
        var definition = new PointAddressDefinition { Area = area };
        if (area == "Simulation")
        {
            definition.LogicalAddress = parameters.Trim();
            return definition;
        }

        // 规范参数优先采用 key=value；S7 手册地址保留原文，交由对应驱动 Profile 做最终解析。
        var tokens = parameters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsedAny = false;
        foreach (var token in tokens)
        {
            var parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                errors.Add($"地址参数不是有效整数：{token}");
                continue;
            }

            switch (parts[0].Trim().ToLowerInvariant())
            {
                case "offset": definition.Offset = number; parsedAny = true; break;
                case "bit":
                case "bitindex": definition.BitIndex = number; parsedAny = true; break;
                case "db":
                case "dbnumber": definition.DbNumber = number; parsedAny = true; break;
                case "byte":
                case "byteoffset": definition.ByteOffset = number; parsedAny = true; break;
                case "bitoffset": definition.BitOffset = number; parsedAny = true; break;
                default: errors.Add($"地址参数字段不受支持：{parts[0]}"); break;
            }
        }

        if (!parsedAny && int.TryParse(parameters, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
        {
            definition.Offset = offset;
            parsedAny = true;
        }

        if (!parsedAny && area.StartsWith("S7.", StringComparison.Ordinal))
            definition.LogicalAddress = parameters.Trim();
        else if (!parsedAny)
            errors.Add("地址参数必须使用整数或 key=value;key=value 规范格式");
        return definition;
    }

    private static DevicePointDataType ReadRawDataType(string value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return DevicePointDataType.Unknown;
        if (DevicePointTemplateDefinition.TryMapDataType(value, out var dataType))
            return dataType;
        errors.Add($"原始数据类型不受支持：{value}（请从模板下拉框选择）");
        return DevicePointDataType.Unknown;
    }

    private static ByteOrder ReadByteOrder(string value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return ByteOrder.BigEndian;
        if (DevicePointTemplateDefinition.TryMapByteOrder(value, out var order)) return order;
        errors.Add($"字节序不受支持：{value}（请填写 大端 或 小端）");
        return ByteOrder.BigEndian;
    }

    private static WordOrder ReadWordOrder(string value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return WordOrder.None;
        if (DevicePointTemplateDefinition.TryMapWordOrder(value, out var order)) return order;
        errors.Add($"字序不受支持：{value}（请填写 无、高字在前 或 低字在前）");
        return WordOrder.None;
    }

    private static void ValidateImportedV3(IReadOnlyList<PointsConfig.PointEntry> points)
    {
        var groups = new List<PointsConfig.PointGroupEntry>();
        var groupIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            var deviceId = DeviceConfigurationMigrator.StableId($"import-device|{point.DeviceCode.Trim()}");
            var groupCode = string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode.Trim();
            var groupKey = $"{deviceId}\u001f{groupCode}";
            if (groupIds.ContainsKey(groupKey)) continue;
            var groupId = DeviceConfigurationMigrator.StableId($"import-group|{deviceId}|{groupCode}");
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
            var deviceId = DeviceConfigurationMigrator.StableId($"import-device|{point.DeviceCode.Trim()}");
            var groupCode = string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode.Trim();
            var clone = new PointsConfig.PointEntry
            {
                Id = string.IsNullOrWhiteSpace(point.Id)
                    ? DeviceConfigurationMigrator.StableId($"import-point|{point.DeviceCode}|{point.Code}|{point.Address}")
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
                RawDataType = point.RawDataType,
                DecodeOptions = point.DecodeOptions,
                WritePolicy = point.WritePolicy,
                Unit = point.Unit,
                RawMin = point.RawMin,
                RawMax = point.RawMax,
                EngMin = point.EngMin,
                EngMax = point.EngMax,
                IsWritable = point.IsWritable,
                IsEnabled = point.IsEnabled,
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
    /// 把客户填写的通信方式转换为运行时使用的协议值。
    /// </summary>
    private static DevicePointProtocol ReadProtocol(string value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return DevicePointProtocol.Unknown;
        if (DevicePointTemplateDefinition.TryMapProtocol(value, out var protocol))
            return protocol;

        errors.Add($"通信方式不受支持：{value}（请从模板下拉框选择：{string.Join("、", DevicePointTemplateDefinition.ProtocolChoices.Select(choice => choice.DisplayValue))}）");
        return DevicePointProtocol.Unknown;
    }

    /// <summary>
    /// 把客户填写的数据类型转换为运行时使用的类型值。
    /// </summary>
    private static DevicePointDataType ReadDataType(string value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return DevicePointDataType.Unknown;
        if (DevicePointTemplateDefinition.TryMapDataType(value, out var dataType))
            return dataType;

        errors.Add($"数据类型不受支持：{value}（请从模板下拉框选择：{string.Join("、", DevicePointTemplateDefinition.DataTypeChoices.Select(choice => choice.DisplayValue))}）");
        return DevicePointDataType.Unknown;
    }

    /// <summary>
    /// 解析布尔值，客户模板推荐填写中文“是/否”，同时兼容旧配置文件。
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
                errors.Add($"{label}不是有效值：{value}（请填写 是 或 否）");
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
                errors.Add($"写入风险不受支持：{value}（请填写 普通 或 高风险）");
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
