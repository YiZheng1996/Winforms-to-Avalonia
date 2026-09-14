using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

public enum DevicePointClipboardMode
{
    Copy,
    Cut
}

/// <summary>
/// 设备点位内存剪贴板。只在当前进程内有效，不写系统剪贴板或配置文件。
/// </summary>
public sealed record DevicePointClipboardPayload(
    DevicePointClipboardMode Mode,
    string SourcePointId,
    string SourceDeviceId,
    string SourceGroupId,
    PointsConfig.PointEntry Snapshot);

/// <summary>
/// 粘贴前交给点位编辑器的候选草稿。
/// </summary>
public sealed record DevicePointPasteDraft(
    DevicePointClipboardMode Mode,
    string SourcePointId,
    string SourceDeviceId,
    string SourceGroupId,
    PointsConfig.PointEntry Snapshot,
    DevicePointDeviceChoice TargetDevice,
    DevicePointGroupChoice? TargetGroup);

public sealed partial class DevicePointManagementViewModel
{
    private DevicePointClipboardPayload? _pointClipboard;

    public bool CanCopySelectedPoint => IsGroupedConfiguration
        && CanEdit
        && !IsBusy
        && SelectedPoint is not null;

    public bool CanCutSelectedPoint => CanCopySelectedPoint;

    public bool CanPastePoint => IsGroupedConfiguration
        && CanEdit
        && !IsBusy
        && _pointClipboard is not null
        && ResolvePasteTarget() is not null;

    /// <summary>
    /// 复制当前点位到内存剪贴板，保留源点位不变。
    /// </summary>
    public OperationFeedback CopySelectedPoint()
    {
        if (!CanCopySelectedPoint || SelectedPoint is null)
            return SetFeedback(false, "请先选择一个可复制的点位");
        _pointClipboard = CreateClipboardPayload(DevicePointClipboardMode.Copy, SelectedPoint);
        NotifyOperationStateChanged();
        return SetFeedback(true, $"已复制点位“{SelectedPoint.PointName}”，可在设备或分组范围粘贴");
    }

    /// <summary>
    /// 剪切当前点位到内存剪贴板，应用成功前不删除点位。
    /// </summary>
    public OperationFeedback CutSelectedPoint()
    {
        if (!CanCutSelectedPoint || SelectedPoint is null)
            return SetFeedback(false, "请先选择一个可剪切的点位");
        _pointClipboard = CreateClipboardPayload(DevicePointClipboardMode.Cut, SelectedPoint);
        NotifyOperationStateChanged();
        return SetFeedback(true, $"已剪切点位“{SelectedPoint.PointName}”，请选择同一设备的目标分组粘贴");
    }

    /// <summary>
    /// 根据当前树范围生成粘贴草稿。剪切跨设备时返回 null 并给出明确原因。
    /// </summary>
    public DevicePointPasteDraft? CreatePasteDraft()
    {
        if (_pointClipboard is null)
        {
            SetFeedback(false, "剪贴板没有可粘贴的点位");
            return null;
        }

        var target = ResolvePasteTarget();
        if (target is null)
        {
            SetFeedback(false, "请先选择目标设备或分组范围");
            return null;
        }

        var resolvedTarget = target.Value;
        var payload = _pointClipboard;
        if (payload.Mode == DevicePointClipboardMode.Cut
            && !string.Equals(payload.SourceDeviceId, resolvedTarget.Device.Id, StringComparison.OrdinalIgnoreCase))
        {
            SetFeedback(false, "剪切粘贴只能在同一设备内移动；跨设备请使用复制");
            return null;
        }

        return new DevicePointPasteDraft(
            payload.Mode,
            payload.SourcePointId,
            payload.SourceDeviceId,
            payload.SourceGroupId,
            payload.Snapshot,
            CreateDeviceChoice(resolvedTarget.Device),
            resolvedTarget.Group is null
                ? null
                : new DevicePointGroupChoice(
                    resolvedTarget.Group.Id,
                    resolvedTarget.Group.DeviceId,
                    resolvedTarget.Group.Code,
                    resolvedTarget.Group.Name,
                    resolvedTarget.Group.SortOrder));
    }

    /// <summary>
    /// 应用由编辑器确认的粘贴结果。Copy 生成新 PointId，Cut 只调整同一个点位的分组。
    /// </summary>
    public Task<OperationFeedback> ApplyPasteAsync(
        DevicePointDialogResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return RunMutationAsync(async token =>
        {
            var payload = _pointClipboard
                ?? throw new DomainException("剪贴板没有可粘贴的点位");
            var state = CaptureUiState();

            if (payload.Mode == DevicePointClipboardMode.Cut)
            {
                if (!string.Equals(payload.SourceDeviceId, result.DeviceId, StringComparison.OrdinalIgnoreCase))
                    throw new DomainException("剪切粘贴只能在同一设备内移动；跨设备请使用复制");
                var entries = _allEntries.Select(ClonePoint).ToList();
                var index = entries.FindIndex(point =>
                    string.Equals(point.Id, payload.SourcePointId, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    throw new DomainException("源点位已不存在，剪切粘贴未应用");
                entries[index].GroupId = ResolveGroupIdentifier(result.GroupId, result.DeviceId);
                entries[index].GroupCode = ResolveGroupCode(entries[index].GroupId);
                var message = await SaveEntriesCoreAsync(
                    entries,
                    "点位已移动到目标分组",
                    _allGroups,
                    new UiRestoreIntent(
                        DevicePointTreeNodeViewModel.CreateNodeKey(
                            DevicePointTreeNodeKind.Group,
                            result.GroupId,
                            result.DeviceId),
                        payload.SourcePointId),
                    token,
                    result.S7OptimizedBlockAccessConfirmed);
                _pointClipboard = null;
                NotifyOperationStateChanged();
                return message;
            }

            var candidate = result.ToEntry();
            if (string.IsNullOrWhiteSpace(candidate.Id))
                candidate.Id = Guid.NewGuid().ToString("D");
            candidate.GroupId = ResolveGroupIdentifier(candidate.GroupId, candidate.DeviceId);
            candidate.GroupCode = ResolveGroupCode(candidate.GroupId);
            var copyEntries = _allEntries.Select(ClonePoint).ToList();
            copyEntries.Add(candidate);
            return await SaveEntriesCoreAsync(
                copyEntries,
                "点位已粘贴为新增点位",
                _allGroups,
                new UiRestoreIntent(SelectedTreeNode?.NodeKey, candidate.Id),
                token,
                result.S7OptimizedBlockAccessConfirmed);
        }, ct);
    }

    private DevicePointClipboardPayload CreateClipboardPayload(
        DevicePointClipboardMode mode,
        DevicePointRow row)
    {
        var source = ClonePoint(row.Entry);
        var sourceDeviceId = ResolveDeviceId(source);
        var sourceGroupId = source.GroupId?.Trim() ?? string.Empty;
        var snapshot = ClonePoint(source);
        snapshot.Id = string.Empty;
        snapshot.DeviceId = sourceDeviceId;
        snapshot.DeviceCode = ResolveDeviceCode(source);
        snapshot.GroupCode = ResolveGroupCode(sourceGroupId);
        return new DevicePointClipboardPayload(
            mode,
            source.Id,
            sourceDeviceId,
            sourceGroupId,
            snapshot);
    }

    private (DeviceConfig.DeviceEntry Device, PointsConfig.PointGroupEntry? Group)? ResolvePasteTarget()
    {
        var node = SelectedTreeNode;
        if (node is null)
            return null;

        var deviceId = node.OwnerDeviceId;
        if (node.Kind == DevicePointTreeNodeKind.Device)
            deviceId = node.Id;
        else if (node.Kind == DevicePointTreeNodeKind.Group)
            deviceId = node.OwnerDeviceId;

        var device = FindConfiguredDevice(deviceId);
        if (device is null)
            return null;

        PointsConfig.PointGroupEntry? group = null;
        if (node.Kind == DevicePointTreeNodeKind.Group)
        {
            group = _allGroups.FirstOrDefault(item =>
                string.Equals(item.Id, node.Id, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            group = _allGroups.FirstOrDefault(item =>
                string.Equals(item.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase));
        }

        return group is null && node.Kind == DevicePointTreeNodeKind.Device
            ? (device, null)
            : (device, group);
    }

    private string ResolveGroupIdentifier(string? groupId, string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(groupId)
            && !string.Equals(groupId.Trim(), "DEFAULT", StringComparison.OrdinalIgnoreCase))
            return groupId.Trim();
        return _allGroups.FirstOrDefault(group =>
                   string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))?.Id
               ?? groupId?.Trim()
               ?? string.Empty;
    }

    private string ResolveGroupCode(string? groupId)
        => _allGroups.FirstOrDefault(group =>
            string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase))?.Code?.Trim()
           ?? "DEFAULT";
}
