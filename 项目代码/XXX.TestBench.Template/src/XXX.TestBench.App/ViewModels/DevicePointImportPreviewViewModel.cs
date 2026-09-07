using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 导入预览中的客户可读点位行。
/// </summary>
public sealed class DevicePointImportPreviewRow(
    PointsConfig.PointEntry entry,
    IReadOnlyList<PointsConfig.PointEntry> currentPoints,
    IReadOnlyList<PointsConfig.PointGroupEntry> currentGroups)
{
    public string Code { get; } = entry.Code;
    public string Name { get; } = entry.Name;
    public string Device { get; } = string.IsNullOrWhiteSpace(entry.DeviceCode) ? "未解析" : entry.DeviceCode;
    public string Group { get; } = ResolveGroup(entry, currentGroups);
    public string Protocol { get; } = DevicePointTypeCatalog.ToDisplayName(entry.ProtocolKind, entry.Protocol);
    public string Address { get; } = entry.Address;
    public string DataType { get; } = DevicePointTypeCatalog.ToDisplayName(entry.DataTypeKind, entry.DataType);
    public string WritePolicy { get; } = !entry.IsWritable
            ? "只读"
            : entry.RiskLevel == WriteRiskLevel.HighRisk
                ? "可写（高风险）"
                : "可写（普通）";
    public string Enabled { get; } = entry.IsEnabled ? "启用" : "停用";
    public string Operation { get; } = ResolveOperation(entry, currentPoints);
    public string Conflict { get; } = ResolveConflict(entry, currentPoints, currentGroups);

    private static string ResolveOperation(
        PointsConfig.PointEntry entry,
        IReadOnlyList<PointsConfig.PointEntry> currentPoints)
    {
        if (!string.IsNullOrWhiteSpace(entry.Id))
        {
            var byId = currentPoints.FirstOrDefault(point =>
                string.Equals(point.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return IsGroupChanged(entry, byId) ? "更新分组" : "更新";
        }
        if (!string.IsNullOrWhiteSpace(entry.DeviceCode)
            && currentPoints.FirstOrDefault(point =>
                string.Equals(point.DeviceCode, entry.DeviceCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(point.Code, entry.Code, StringComparison.OrdinalIgnoreCase)) is { } byDevice)
            return IsGroupChanged(entry, byDevice) ? "更新分组" : "更新";
        return currentPoints.Any(point => string.Equals(point.Code, entry.Code, StringComparison.OrdinalIgnoreCase))
            ? "冲突"
            : "新增";
    }

    private static string ResolveConflict(
        PointsConfig.PointEntry entry,
        IReadOnlyList<PointsConfig.PointEntry> currentPoints,
        IReadOnlyList<PointsConfig.PointGroupEntry> currentGroups)
        => currentPoints.Any(point => string.Equals(point.Code, entry.Code, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(point.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
            ? "点位编码已存在，请核对点位标识/设备归属"
            : string.Empty;

    private static bool IsGroupChanged(PointsConfig.PointEntry incoming, PointsConfig.PointEntry current)
        => !string.IsNullOrWhiteSpace(incoming.GroupId)
            && !string.Equals(incoming.GroupId, current.GroupId, StringComparison.OrdinalIgnoreCase);

    private static string ResolveGroup(
        PointsConfig.PointEntry entry,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
    {
        var group = groups.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(entry.GroupId)
            && string.Equals(item.Id, entry.GroupId, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
            return $"{group.Code}  {group.Name}";
        return string.IsNullOrWhiteSpace(entry.GroupCode) ? "DEFAULT  未分组" : entry.GroupCode;
    }
}

/// <summary>
/// 导入覆盖前的预览模型，明确告知用户导入是整体替换而非追加。
/// </summary>
public sealed class DevicePointImportPreviewViewModel
{
    public DevicePointImportPreviewViewModel(
        IReadOnlyList<PointsConfig.PointEntry> points,
        int currentPointCount,
        IReadOnlyList<PointsConfig.PointEntry>? currentPoints = null,
        IReadOnlyList<PointsConfig.PointGroupEntry>? currentGroups = null)
    {
        PointCount = points.Count;
        CurrentPointCount = currentPointCount;
        var existing = currentPoints ?? Array.Empty<PointsConfig.PointEntry>();
        var groups = currentGroups ?? Array.Empty<PointsConfig.PointGroupEntry>();
        Rows = points
            .Take(100)
            .Select(point => new DevicePointImportPreviewRow(point, existing, groups))
            .ToList();
        ConflictCount = Rows.Count(row => row.Operation == "冲突");
    }

    public int PointCount { get; }
    public int CurrentPointCount { get; }
    public IReadOnlyList<DevicePointImportPreviewRow> Rows { get; }
    public int ConflictCount { get; }
    public bool CanApply => ConflictCount == 0;

    public string SummaryText =>
        $"文件已通过格式、设备归属和分组校验，共 {PointCount} 个点位；当前目录有 {CurrentPointCount} 个点位；预览中冲突 {ConflictCount} 个。";

    public string ReplacementText =>
        pointsAreV2
            ? (PointCount > Rows.Count
                ? $"下面显示前 {Rows.Count} 条预览，确认后将合并新增/更新文件中的 {PointCount} 个点位，未出现的其他点位保持不变。"
                : "确认后将合并新增/更新文件中的点位，未出现在文件中的其他设备点位保持不变。")
            : (PointCount > Rows.Count
                ? $"下面显示前 {Rows.Count} 条预览，确认后将用文件中的 {PointCount} 个点位整体替换当前目录。"
                : "确认后将用文件中的点位整体替换当前目录，不会追加到现有点位后面。");

    public string SafetyText =>
        ConflictCount > 0
            ? "预览存在冲突，不能应用；请修正点位标识、设备编码或点位编码后重新导入。"
            : "请确认设备编码、地址参数、原始数据类型和写入权限无误；点击确认后才会保存，取消则当前目录不变。";

    private bool pointsAreV2 => Rows.Any(row => row.Device != "未解析");
}
