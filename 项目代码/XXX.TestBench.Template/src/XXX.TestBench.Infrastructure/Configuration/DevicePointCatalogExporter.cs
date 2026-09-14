using ClosedXML.Excel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 把当前页面范围的真实点位导出为后续可重新导入的 v6 Excel 文件。
/// 只写模板明确承载的十一列，不写内部身份、隐藏安全字段或运行时值。
/// </summary>
public sealed class DevicePointCatalogExporter : IDevicePointCatalogExporter
{
    public Task ExportAsync(
        string filePath,
        DevicePointCatalogExportRequest request,
        CancellationToken ct = default)
        => Task.Run(() => ExportCore(filePath, request, ct), ct);

    private static void ExportCore(
        string filePath,
        DevicePointCatalogExportRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("点位导出路径不能为空", nameof(filePath));
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("设备点位只能导出为 .xlsx 文件");
        if (request.Points.Count == 0)
            throw new InvalidOperationException("当前范围没有可导出的点位");

        var columns = DevicePointTemplateDefinition.Columns;
        var devices = (request.Devices ?? Array.Empty<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();
        var groups = (request.Groups ?? Array.Empty<PointsConfig.PointGroupEntry>())
            .Where(group => group is not null)
            .ToList();
        var deviceById = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var deviceByCode = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Code))
            .GroupBy(device => device.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var groupById = groups
            .Where(group => !string.IsNullOrWhiteSpace(group.Id))
            .GroupBy(group => group.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var referencedDeviceIds = request.Points
            .Select(point => ResolveDevice(point, deviceById, deviceByCode)?.Id?.Trim() ?? point.DeviceId?.Trim() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var multipleDevices = referencedDeviceIds.Count > 1;

        using var workbook = new XLWorkbook();
        var dataSheet = workbook.AddWorksheet(DevicePointTemplateDefinition.DataSheetName);
        WriteHeader(dataSheet, columns);
        ConfigureDataSheet(dataSheet, columns);

        var rowIndex = DevicePointTemplateDefinition.FirstDataRow;
        foreach (var point in request.Points)
        {
            ct.ThrowIfCancellationRequested();
            var device = ResolveDevice(point, deviceById, deviceByCode);
            var group = ResolveGroup(point, groups, groupById);
            var values = new[]
            {
                FormatPointTag(point, device, group, multipleDevices),
                point.Address ?? string.Empty,
                DevicePointTypeCatalog.ToDisplayName(point.RawDataTypeKind, point.RawDataType),
                FormatByteOrder(point),
                FormatWordOrder(point),
                point.IsWritable ? "读写" : "只读",
                FormatScale(point.EffectiveRawMin),
                FormatScale(point.EffectiveRawMax),
                FormatScale(point.EffectiveEngMin),
                FormatScale(point.EffectiveEngMax),
                point.Description ?? string.Empty
            };

            for (var columnIndex = 0; columnIndex < values.Length; columnIndex++)
                dataSheet.Cell(rowIndex, columnIndex + 1).Value = values[columnIndex];
            rowIndex++;
        }

        var instructions = workbook.AddWorksheet(DevicePointTemplateDefinition.InstructionsSheetName);
        instructions.Cell(1, 1).Value = "设备点位当前范围导出";
        instructions.Cell(1, 1).Style.Font.Bold = true;
        instructions.Cell(2, 1).Value = $"范围：{request.ScopeDisplayName}";
        instructions.Cell(3, 1).Value = "用途：批量维护后可重新通过“导入点位”校验和预览。";
        instructions.Cell(4, 1).Value = "边界：不是完整配置备份；写入策略和风险等级等隐藏字段不会被本文件覆盖，Modbus 字节序和字序按当前模板导出。";
        instructions.Cell(5, 1).Value = "注意：导入更新已有可写点位时保留原风险等级；导入把点位改为只读时风险等级归一化为普通。";
        instructions.Column(1).Width = 100;
        instructions.Range(2, 1, 5, 1).Style.Alignment.WrapText = true;

        workbook.SaveAs(filePath);
    }

    private static void WriteHeader(
        IXLWorksheet worksheet,
        IReadOnlyList<DevicePointTemplateDefinition.ColumnDefinition> columns)
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
            comment.AddText(column.Description);
            comment.SetAuthor("设备点位当前范围导出");
        }
    }

    private static void ConfigureDataSheet(
        IXLWorksheet worksheet,
        IReadOnlyList<DevicePointTemplateDefinition.ColumnDefinition> columns)
    {
        worksheet.SheetView.FreezeRows(1);
        worksheet.Row(1).Height = 36;
        worksheet.Range(1, 1, 1, columns.Count).SetAutoFilter();
        for (var index = 0; index < columns.Count; index++)
            worksheet.Column(index + 1).Width = columns[index].Field switch
            {
                "PointTag" => 36,
                "Address" => 24,
                "DataType" => 18,
                "ByteOrder" or "WordOrder" => 20,
                "Access" => 18,
                "RawMin" or "RawMax" or "EngMin" or "EngMax" => 14,
                "Description" => 36,
                _ => 18
            };
    }

    private static DeviceConfig.DeviceEntry? ResolveDevice(
        PointsConfig.PointEntry point,
        IReadOnlyDictionary<string, DeviceConfig.DeviceEntry> byId,
        IReadOnlyDictionary<string, DeviceConfig.DeviceEntry> byCode)
    {
        if (!string.IsNullOrWhiteSpace(point.DeviceId) && byId.TryGetValue(point.DeviceId.Trim(), out var device))
            return device;
        if (!string.IsNullOrWhiteSpace(point.DeviceCode) && byCode.TryGetValue(point.DeviceCode.Trim(), out device))
            return device;
        return null;
    }

    private static PointsConfig.PointGroupEntry? ResolveGroup(
        PointsConfig.PointEntry point,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        IReadOnlyDictionary<string, PointsConfig.PointGroupEntry> byId)
    {
        if (!string.IsNullOrWhiteSpace(point.GroupId) && byId.TryGetValue(point.GroupId.Trim(), out var group))
            return group;
        return groups.FirstOrDefault(item =>
            string.Equals(item.DeviceId, point.DeviceId, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(item.Code, point.GroupCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Id, point.GroupId, StringComparison.OrdinalIgnoreCase)));
    }

    private static string FormatPointTag(
        PointsConfig.PointEntry point,
        DeviceConfig.DeviceEntry? device,
        PointsConfig.PointGroupEntry? group,
        bool multipleDevices)
    {
        var pointName = string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name.Trim();
        var groupCode = group?.Code?.Trim() ?? point.GroupCode?.Trim() ?? string.Empty;
        var tag = string.IsNullOrWhiteSpace(groupCode)
            || string.Equals(groupCode, "DEFAULT", StringComparison.OrdinalIgnoreCase)
            ? pointName
            : $"{groupCode}.{pointName}";
        var deviceCode = device?.Code?.Trim() ?? point.DeviceCode?.Trim() ?? string.Empty;
        return multipleDevices && !string.IsNullOrWhiteSpace(deviceCode)
            ? $"{deviceCode}/{tag}"
            : tag;
    }

    private static string FormatScale(decimal? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// v6 模板显式承载 Modbus 寄存器字节序；非 Modbus 点位留空，避免把
    /// S7/仿真的默认行为伪装成 Modbus 配置。历史配置缺少该对象时仍按
    /// Modbus 首版约定输出大端，随后由导入器继续进行驱动级校验。
    /// </summary>
    private static string FormatByteOrder(PointsConfig.PointEntry point)
    {
        if (point.ProtocolKind is not (DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp))
            return string.Empty;

        // DecodeOptions 是当前模型的默认字段；如果读取到更早的对象而该字段为空，
        // 仍按方案规定的 Modbus 初始大端导出，避免导出的 v6 文件丢失可执行语义。
        return point.DecodeOptions?.ByteOrder switch
        {
            null or ByteOrder.BigEndian => "大端",
            ByteOrder.LittleEndian => "小端",
            _ => string.Empty
        };
    }

    /// <summary>
    /// v6 模板显式承载 Modbus 多寄存器字序；单寄存器和位点输出“无”。
    /// 空值不擅自猜测，留给导入预览/驱动校验报告明确错误。
    /// </summary>
    private static string FormatWordOrder(PointsConfig.PointEntry point)
        => point.ProtocolKind is DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp
            ? point.DecodeOptions?.WordOrder switch
            {
                WordOrder.None => "无",
                WordOrder.HighWordFirst => "高字在前",
                WordOrder.LowWordFirst => "低字在前",
                _ => string.Empty
            }
            : string.Empty;
}
