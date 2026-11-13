using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 导入预览中的客户可读点位行。所有操作和冲突均来自统一 DevicePointImportPlan。
/// </summary>
public sealed class DevicePointImportPreviewRow
{
    private DevicePointImportPreviewRow(
        DevicePointImportPlanRow planRow,
        PointsConfig.PointEntry entry,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        int rowNumber)
    {
        RowNumber = rowNumber > 0 ? rowNumber : planRow.RowNumber;
        Code = entry.Code;
        Name = entry.Name;
        PointTag = GetPointTag(entry, groups);
        Device = string.IsNullOrWhiteSpace(entry.DeviceCode) ? "未解析" : entry.DeviceCode;
        Group = ResolveGroup(entry, groups);
        Protocol = DevicePointTypeCatalog.ToDisplayName(entry.ProtocolKind, entry.Protocol);
        OriginalAddress = string.IsNullOrWhiteSpace(entry.OriginalAddress)
            ? entry.Address
            : entry.OriginalAddress;
        CanonicalAddress = GetCanonicalAddress(entry);
        ModbusArea = entry.AddressDefinition?.Area ?? string.Empty;
        ModbusOffset = entry.AddressDefinition?.Offset?.ToString() ?? string.Empty;
        DataType = DevicePointTypeCatalog.ToDisplayName(entry.RawDataTypeKind, entry.RawDataType);
        ByteOrder = entry.DecodeOptions?.ByteOrder.ToString() ?? string.Empty;
        ByteOrderSource = entry.ByteOrderSource;
        WordOrder = entry.DecodeOptions?.WordOrder.ToString() ?? string.Empty;
        WordOrderSource = entry.WordOrderSource;
        WritePolicy = entry.IsWritable ? "读写" : "只读";
        Operation = FormatOperation(planRow.Operation);
        Conflict = FormatConflict(RowNumber, planRow.Issues);
    }

    /// <summary>
    /// 兼容现有调用方的单行预览构造；与页面导入共用同一个 Planner。
    /// </summary>
    public DevicePointImportPreviewRow(
        PointsConfig.PointEntry entry,
        IReadOnlyList<PointsConfig.PointEntry> currentPoints,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        int rowNumber = 0)
        : this(
            new DevicePointImportPlanner()
                .Build(new[] { new DevicePointImportRow(rowNumber, entry) }, currentPoints, groups)
                .Rows
                .Single(),
            entry,
            groups,
            rowNumber)
    {
    }

    internal DevicePointImportPreviewRow(
        DevicePointImportPlanRow planRow,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        int rowNumber = 0)
        : this(
            planRow,
            planRow.Candidate ?? planRow.Incoming,
            groups,
            rowNumber)
    {
    }

    public int RowNumber { get; }
    public string Code { get; }
    public string Name { get; }
    public string PointTag { get; }
    public string Device { get; }
    public string Group { get; }
    public string Protocol { get; }
    public string OriginalAddress { get; }
    public string Address => OriginalAddress;
    public string CanonicalAddress { get; }
    public string ModbusArea { get; }
    public string ModbusOffset { get; }
    public string DataType { get; }
    public string ByteOrder { get; }
    public string ByteOrderSource { get; }
    public string WordOrder { get; }
    public string WordOrderSource { get; }
    public string WritePolicy { get; }
    public string Operation { get; }
    public string Conflict { get; }

    public string OperationBackground => Operation switch
    {
        "新增" => "#EAF2FF",
        "更新" or "更新分组" => "#EAF8F1",
        "冲突" => "#FFF0EE",
        _ => "#F2F4F7"
    };

    public string OperationForeground => Operation switch
    {
        "新增" => "#0758D7",
        "更新" or "更新分组" => "#147A45",
        "冲突" => "#C9362B",
        _ => "#667085"
    };

    public string ConflictForeground => string.IsNullOrWhiteSpace(Conflict) ? "#98A2B3" : "#C9362B";

    private static string FormatOperation(DevicePointImportOperationKind operation)
        => operation switch
        {
            DevicePointImportOperationKind.Add => "新增",
            DevicePointImportOperationKind.Update => "更新",
            DevicePointImportOperationKind.MoveGroup => "更新分组",
            DevicePointImportOperationKind.Unchanged => "无变化",
            _ => "冲突"
        };

    private static string FormatConflict(int rowNumber, IReadOnlyList<string> issues)
    {
        if (issues.Count == 0)
            return string.Empty;
        var text = string.Join("；", issues.Distinct(StringComparer.OrdinalIgnoreCase));
        return rowNumber > 0 ? $"第 {rowNumber} 行：{text}" : text;
    }

    private static string GetPointTag(
        PointsConfig.PointEntry entry,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
    {
        var groupCode = entry.GroupCode?.Trim();
        if (string.IsNullOrWhiteSpace(groupCode))
        {
            groupCode = groups.FirstOrDefault(group =>
                string.Equals(group.Id, entry.GroupId, StringComparison.OrdinalIgnoreCase))?.Code;
        }
        return DevicePointTag.Format(
            groupCode,
            string.IsNullOrWhiteSpace(entry.Name) ? entry.Code : entry.Name);
    }

    private static string ResolveGroup(
        PointsConfig.PointEntry entry,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
    {
        var group = groups.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(entry.GroupId)
            && string.Equals(item.Id, entry.GroupId, StringComparison.OrdinalIgnoreCase));
        if (group is not null
            && !string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(group.Name) ? group.Code : group.Name;
        return string.IsNullOrWhiteSpace(entry.GroupCode)
            || string.Equals(entry.GroupCode, "DEFAULT", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : entry.GroupCode;
    }

    private static string GetCanonicalAddress(PointsConfig.PointEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.CanonicalAddress))
            return entry.CanonicalAddress;
        if (entry.AddressDefinition is { Area: { Length: > 0 }, Offset: { } offset })
            return $"{entry.AddressDefinition.Area}:{offset}";
        return entry.Address;
    }
}

/// <summary>
/// 导入覆盖前的预览模型：只消费统一 Plan 的 Rows 和统计。
/// </summary>
public sealed class DevicePointImportPreviewViewModel
{
    public const int PreviewRowLimit = 100;

    private readonly DevicePointImportPlan _plan;
    private readonly IReadOnlyList<PointsConfig.PointGroupEntry> _groups;
    private readonly bool _mergeIntoCurrentConfiguration;

    public DevicePointImportPreviewViewModel(
        DevicePointImportPlan plan,
        int currentPointCount = 0,
        IReadOnlyList<PointsConfig.PointGroupEntry>? groups = null,
        IReadOnlyList<DeviceConfig.DeviceEntry>? devices = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _groups = groups ?? Array.Empty<PointsConfig.PointGroupEntry>();
        PointCount = plan.Rows.Count;
        CurrentPointCount = currentPointCount;
        _mergeIntoCurrentConfiguration = plan.Rows.Any(row => row.Existing is not null);
        S7OptimizedBlockAccessNotice = BuildS7OptimizedBlockAccessNotice(plan, devices);
        Rows = BuildRows(plan, _groups);
        AssignCounts(plan);
    }

    /// <summary>
    /// 兼容旧调用方：内部先把输入转换为统一 Plan，再交给同一预览模型。
    /// </summary>
    public DevicePointImportPreviewViewModel(
        IReadOnlyList<PointsConfig.PointEntry> points,
        int currentPointCount,
        IReadOnlyList<PointsConfig.PointEntry>? currentPoints = null,
        IReadOnlyList<PointsConfig.PointGroupEntry>? currentGroups = null,
        IReadOnlyList<DevicePointImportRow>? parsedRows = null,
        IReadOnlyList<DeviceConfig.DeviceEntry>? devices = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        var sourceRows = parsedRows is { Count: > 0 }
            ? parsedRows
            : points.Select((point, index) => new DevicePointImportRow(index + 2, point)).ToList();
        _groups = currentGroups ?? Array.Empty<PointsConfig.PointGroupEntry>();
        _plan = new DevicePointImportPlanner().Build(
            sourceRows,
            currentPoints ?? Array.Empty<PointsConfig.PointEntry>(),
            _groups);
        PointCount = points.Count;
        CurrentPointCount = currentPointCount;
        _mergeIntoCurrentConfiguration = points.Any(point =>
            !string.IsNullOrWhiteSpace(point.DeviceId)
            || !string.IsNullOrWhiteSpace(point.DeviceCode))
            || _plan.Rows.Any(row => row.Existing is not null);
        S7OptimizedBlockAccessNotice = BuildS7OptimizedBlockAccessNotice(_plan, devices);
        Rows = BuildRows(_plan, _groups);
        AssignCounts(_plan);
    }

    public int PointCount { get; }
    public int CurrentPointCount { get; }
    public IReadOnlyList<DevicePointImportPreviewRow> Rows { get; }
    public int ConflictCount { get; private set; }
    public int AddedCount { get; private set; }
    public int UpdatedCount { get; private set; }
    public int UnchangedCount { get; private set; }
    public int HiddenRowCount => Math.Max(0, PointCount - Rows.Count);
    public bool CanApply => _plan.CanApply;
    public SiemensS7OptimizedBlockAccessNotice? S7OptimizedBlockAccessNotice { get; }
    public bool HasS7OptimizedBlockAccessNotice => S7OptimizedBlockAccessNotice is not null;
    public bool RequiresS7OptimizedBlockAccessConfirmation => S7OptimizedBlockAccessNotice is not null;
    public string S7OptimizedBlockAccessShortMessage => S7OptimizedBlockAccessNotice?.ShortMessage ?? string.Empty;
    public string S7OptimizedBlockAccessDetailMessage => S7OptimizedBlockAccessNotice?.DetailMessage ?? string.Empty;

    public string SummaryText =>
        $"文件已通过格式、设备归属和分组校验，共 {PointCount} 个点位；当前目录有 {CurrentPointCount} 个点位。";

    public string ChangeSummaryText =>
        $"新增 {AddedCount} · 更新 {UpdatedCount} · 无变化 {UnchangedCount} · 冲突 {ConflictCount}";

    public string PreviewLimitText => HiddenRowCount > 0
        ? $"当前仅显示前 {Rows.Count} 条，仍已统计全部 {PointCount} 条；请确认冲突数为 0 后再应用。"
        : $"已显示全部 {Rows.Count} 条预览记录。";

    public string ReplacementText =>
        _mergeIntoCurrentConfiguration
            ? $"确认后将合并新增/更新文件中的 {PointCount} 个点位，未出现在文件中的其他点位保持不变。"
            : $"确认后将用文件中的 {PointCount} 个点位整体替换当前目录。";

    public string SafetyText
    {
        get
        {
            if (ConflictCount > 0)
                return "预览存在冲突，不能应用；请按冲突说明修正设备范围、点位标签或地址后重新导入。";
            var preservedRisk = _plan.Rows.Any(row =>
                row.Existing is { IsWritable: true, RiskLevel: WriteRiskLevel.HighRisk }
                && row.Candidate is { IsWritable: true, RiskLevel: WriteRiskLevel.HighRisk });
            var prefix = preservedRisk
                ? "已有高风险点位的风险等级和隐藏写入策略将保留。"
                : "请确认设备、点位标签、地址、数据类型和访问权限无误。";
            return prefix + "点击确认后才会保存，取消则当前目录不变。";
        }
    }

    private static IReadOnlyList<DevicePointImportPreviewRow> BuildRows(
        DevicePointImportPlan plan,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
        => plan.Rows
            .Take(PreviewRowLimit)
            .Select(row => new DevicePointImportPreviewRow(row, groups))
            .ToList();

    private void AssignCounts(DevicePointImportPlan plan)
    {
        ConflictCount = plan.ConflictCount;
        AddedCount = plan.AddedCount;
        UpdatedCount = plan.UpdatedCount + plan.MovedCount;
        UnchangedCount = plan.UnchangedCount;
    }

    private static SiemensS7OptimizedBlockAccessNotice? BuildS7OptimizedBlockAccessNotice(
        DevicePointImportPlan plan,
        IReadOnlyList<DeviceConfig.DeviceEntry>? devices)
    {
        var importedPoints = plan.Rows
            .Select(row => row.Candidate ?? row.Incoming)
            .ToList();
        return SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            importedPoints,
            devices,
            requireHardwareMode: false);
    }
}
