using System.Collections.ObjectModel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备点位页当前范围的单一只读快照。
/// 表格、范围详情、诊断、模板和导出都从同一份快照取数，避免各自重新推断范围。
/// </summary>
public sealed record DevicePointScopeSnapshot(
    string NodeKey,
    string DisplayName,
    bool IsEditable,
    bool CanDiagnose,
    IReadOnlyList<DeviceConfig.DeviceEntry> Devices,
    IReadOnlyList<PointsConfig.PointGroupEntry> Groups,
    IReadOnlyList<PointsConfig.PointEntry> Points)
{
    public static DevicePointScopeSnapshot Empty { get; } = new(
        "root",
        "全部设备",
        false,
        false,
        Array.Empty<DeviceConfig.DeviceEntry>(),
        Array.Empty<PointsConfig.PointGroupEntry>(),
        Array.Empty<PointsConfig.PointEntry>());
}

public sealed partial class DevicePointManagementViewModel
{
    private DevicePointScopeSnapshot _currentScope = DevicePointScopeSnapshot.Empty;

    /// <summary>
    /// 当前左侧范围的只读快照；点位搜索只作用于右侧显示，不改变此范围。
    /// </summary>
    public DevicePointScopeSnapshot CurrentScope => _currentScope;

    private void RefreshCurrentScopeSnapshot()
        => _currentScope = BuildScopeSnapshot(
            _services.DeviceConfig,
            new PointsConfig
            {
                SchemaVersion = _services.DevicePoints.SchemaVersion,
                Points = _allEntries,
                Groups = _allGroups
            },
            SelectedTreeNode,
            CanEdit,
            _services.DeviceModes.Runtime is not null);

    /// <summary>
    /// 从指定的配置快照计算范围。导入预览也使用这条路径，避免在异步过程中
    /// 把当前页面列表和刚加载的 Active 快照混在一起。
    /// </summary>
    private DevicePointScopeSnapshot BuildScopeSnapshot(
        DeviceConfig deviceConfig,
        PointsConfig pointsConfig,
        DevicePointTreeNodeViewModel? node,
        bool canEdit,
        bool hasRuntime)
    {
        var devices = GetScopeDevices(deviceConfig, node);
        var allPoints = pointsConfig.Points ?? new List<PointsConfig.PointEntry>();
        var allGroups = pointsConfig.Groups ?? new List<PointsConfig.PointGroupEntry>();
        var deviceIds = devices
            .Select(GetDeviceIdentity)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<PointsConfig.PointEntry> points;
        if (node is null || node.Kind == DevicePointTreeNodeKind.Root)
        {
            points = allPoints;
        }
        else if (node.IsVirtual || node.IsOrphan)
        {
            points = Array.Empty<PointsConfig.PointEntry>();
        }
        else
        {
            points = node.Kind switch
            {
                DevicePointTreeNodeKind.Channel => allPoints
                    .Where(point => deviceIds.Contains(ResolveScopeDeviceId(point, devices)))
                    .ToList(),
                DevicePointTreeNodeKind.Device => allPoints
                    .Where(point => string.Equals(
                        ResolveScopeDeviceId(point, devices), node.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                DevicePointTreeNodeKind.Group => allPoints
                    .Where(point => deviceIds.Contains(ResolveScopeDeviceId(point, devices))
                        && IsPointInGroup(point, new PointsConfig.PointGroupEntry
                        {
                            Id = node.Id,
                            Code = node.Code
                        }))
                    .ToList(),
                _ => allPoints
            };
        }

        var groups = allGroups
            .Where(group => deviceIds.Contains(group.DeviceId))
            .ToList();
        var nodeKey = node?.NodeKey ?? "root";
        var displayName = node?.DisplayText ?? "全部设备";
        var isGrouped = deviceConfig.SchemaVersion == DeviceConfig.CurrentSchemaVersion
            && pointsConfig.SchemaVersion == PointsConfig.CurrentSchemaVersion;
        var isEditable = isGrouped
            && canEdit
            && node is not { IsVirtual: true }
            && node is not { IsOrphan: true };
        var canDiagnose = hasRuntime
            && points.Any(point => !string.IsNullOrWhiteSpace(point.Id)
                && !string.IsNullOrWhiteSpace(ResolveScopeDeviceId(point, devices)));

        return new DevicePointScopeSnapshot(
            nodeKey,
            displayName,
            isEditable,
            canDiagnose,
            new ReadOnlyCollection<DeviceConfig.DeviceEntry>(devices.ToList()),
            new ReadOnlyCollection<PointsConfig.PointGroupEntry>(groups),
            new ReadOnlyCollection<PointsConfig.PointEntry>(points.ToList()));
    }

    private static List<DeviceConfig.DeviceEntry> GetScopeDevices(
        DeviceConfig deviceConfig,
        DevicePointTreeNodeViewModel? node)
    {
        var devices = (deviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();
        if (node is null || node.Kind == DevicePointTreeNodeKind.Root)
            return devices;
        if (node.IsVirtual || node.IsOrphan)
            return new List<DeviceConfig.DeviceEntry>();

        if (node.Kind is DevicePointTreeNodeKind.Device or DevicePointTreeNodeKind.Group)
            return devices
                .Where(device => string.Equals(
                    GetDeviceIdentity(device), node.OwnerDeviceId ?? node.Id,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (node.Kind == DevicePointTreeNodeKind.Channel)
        {
            var channels = (deviceConfig.Channels ?? new List<ChannelEntry>())
                .Where(channel => channel is not null)
                .ToList();
            var channel = channels.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.Id)
                && string.Equals(item.Id.Trim(), node.Id, StringComparison.OrdinalIgnoreCase));
            channel ??= channels.FirstOrDefault(item =>
                string.IsNullOrWhiteSpace(item.Id)
                && (string.Equals(item.Code, node.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Code, node.Code, StringComparison.OrdinalIgnoreCase)));
            if (channel is null)
                return new List<DeviceConfig.DeviceEntry>();
            var channelId = GetChannelIdentity(channel);
            return devices
                .Where(device => ResolveConfiguredChannel(device, channels) is { } resolved
                    && string.Equals(
                        GetChannelIdentity(resolved), channelId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new List<DeviceConfig.DeviceEntry>();
    }

    private static string ResolveScopeDeviceId(
        PointsConfig.PointEntry point,
        IReadOnlyList<DeviceConfig.DeviceEntry> devices)
    {
        if (!string.IsNullOrWhiteSpace(point.DeviceId))
            return point.DeviceId.Trim();
        var deviceCode = point.DeviceCode?.Trim() ?? string.Empty;
        var device = devices.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(deviceCode)
            && string.Equals(item.Code, deviceCode, StringComparison.OrdinalIgnoreCase));
        return device is null ? string.Empty : GetDeviceIdentity(device);
    }

}
