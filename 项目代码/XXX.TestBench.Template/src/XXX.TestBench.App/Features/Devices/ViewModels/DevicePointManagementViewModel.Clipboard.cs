using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Application;
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
    DevicePointGroupChoice? TargetGroup,
    string SourceDeviceDisplayName = "",
    string SourceGroupDisplayName = "");

public sealed partial class DevicePointManagementViewModel
{
    private DevicePointClipboardPayload? _pointClipboard;

    public bool CanCopySelectedPoint => IsGroupedConfiguration
        && CanEdit
        && CanStartPageOperation
        && SelectedPoint is { HasConfigurationError: false };

    public bool CanCutSelectedPoint => CanCopySelectedPoint;

    public bool CanPastePoint => IsGroupedConfiguration
        && CanEdit
        && CanStartPageOperation
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
            && !string.Equals(payload.SourceDeviceId, GetDeviceIdentity(resolvedTarget.Device), StringComparison.OrdinalIgnoreCase))
        {
            SetFeedback(false, "剪切粘贴只能在同一设备内移动；跨设备请使用复制");
            return null;
        }

        var sourceDevice = FindConfiguredDevice(payload.SourceDeviceId);
        var sourceGroup = _allGroups.FirstOrDefault(group =>
            string.Equals(group.Id, ResolveCanonicalGroupId(payload.SourceGroupId, payload.SourceDeviceId), StringComparison.OrdinalIgnoreCase));
        var sourceDeviceDisplayName = sourceDevice is null
            ? string.IsNullOrWhiteSpace(payload.Snapshot.DeviceCode) ? "源设备" : payload.Snapshot.DeviceCode
            : string.IsNullOrWhiteSpace(sourceDevice.Name) ? sourceDevice.Code : sourceDevice.Name;
        var sourceGroupDisplayName = sourceGroup is null
            || string.Equals(sourceGroup.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase)
            ? "未分组"
            : string.IsNullOrWhiteSpace(sourceGroup.Name) ? sourceGroup.Code : sourceGroup.Name;

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
                     resolvedTarget.Group.SortOrder),
            sourceDeviceDisplayName,
            sourceGroupDisplayName);
    }

    /// <summary>
    /// 应用由编辑器确认的粘贴结果。Copy 生成新 PointId，Cut 只调整同一个点位的分组。
    /// </summary>
    public Task<OperationFeedback> ApplyPasteAsync(
        DevicePointDialogResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var payloadToValidate = _pointClipboard;
        if (payloadToValidate is null)
            return Task.FromResult(SetFeedback(false, "剪贴板没有可粘贴的点位"));

        if (payloadToValidate.Mode == DevicePointClipboardMode.Cut)
        {
            var draft = CreatePasteDraft();
            return draft is null
                ? Task.FromResult(SetFeedback(false, StatusMessage))
                : ApplyCutPasteAsync(draft, ct);
        }

        var target = ResolvePasteTarget(result);
        if (target is null)
            return Task.FromResult(SetFeedback(false, "目标设备或分组已失效，请重新选择粘贴范围"));

        return RunMutationAsync(async token =>
        {
            var payload = _pointClipboard
                ?? throw new DomainException("剪贴板没有可粘贴的点位");
            var state = CaptureUiState();

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
                new DeviceConfigurationApplyOptions(
                    ConfirmS7OptimizedBlockAccess: result.S7OptimizedBlockAccessConfirmed));
        }, ct);
    }

    /// <summary>
    /// 剪切确认只接收稳定源身份和目标设备/分组，不读取或保存弹窗中的可编辑字段。
    /// </summary>
    public Task<OperationFeedback> ApplyCutPasteAsync(
        DevicePointPasteDraft draft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Mode != DevicePointClipboardMode.Cut)
            return Task.FromResult(SetFeedback(false, "当前剪贴板不是剪切操作"));

        var payload = _pointClipboard;
        if (payload is null
            || payload.Mode != DevicePointClipboardMode.Cut
            || !string.Equals(payload.SourcePointId, draft.SourcePointId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payload.SourceDeviceId, draft.SourceDeviceId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(SetFeedback(false, "剪切源已变化，请重新选择点位后再移动"));

        var targetDeviceId = GetDeviceIdentityFromChoice(draft.TargetDevice);
        var targetGroupId = draft.TargetGroup?.Id?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetGroupId))
            targetGroupId = ResolveGroupIdentifier("DEFAULT", targetDeviceId);
        if (string.IsNullOrWhiteSpace(targetGroupId))
            return Task.FromResult(SetFeedback(false, "目标设备没有可用的默认分组，剪切粘贴未应用"));

        return MovePointToGroupAsync(
            draft.SourcePointId,
            draft.SourceDeviceId,
            targetDeviceId,
            targetGroupId,
            clearCutClipboard: true,
            successText: "点位已移动到目标分组",
            ct);
    }

    private DevicePointClipboardPayload CreateClipboardPayload(
        DevicePointClipboardMode mode,
        DevicePointRow row)
    {
        // 只在剪贴板边界克隆一次；之后所有字段均来自这一份完整快照，
        // 避免地址、解码、量程、写入策略等元数据在多次复制中被遗漏。
        var snapshot = ClonePoint(row.Entry);
        var sourcePointId = snapshot.Id?.Trim() ?? string.Empty;
        var sourceDeviceId = ResolveDeviceId(snapshot);
        var sourceGroupId = snapshot.GroupId?.Trim() ?? string.Empty;
        snapshot.Id = string.Empty;
        snapshot.DeviceId = sourceDeviceId;
        snapshot.DeviceCode = ResolveDeviceCode(row.Entry);
        snapshot.GroupCode = ResolveGroupCode(sourceGroupId);
        return new DevicePointClipboardPayload(
            mode,
            sourcePointId,
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
            if (group is not null
                && !string.Equals(group.DeviceId, GetDeviceIdentity(device), StringComparison.OrdinalIgnoreCase))
                return null;
        }
        else
        {
            group = _allGroups.FirstOrDefault(item =>
                string.Equals(item.DeviceId, GetDeviceIdentity(device), StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase));
        }

        return group is null ? null : (device, group);
    }

    private (DeviceConfig.DeviceEntry Device, PointsConfig.PointGroupEntry? Group)? ResolvePasteTarget(
        DevicePointDialogResult result)
    {
        var device = FindConfiguredDevice(result.DeviceId);
        if (device is null)
            return null;
        var groupId = result.GroupId?.Trim() ?? string.Empty;
        PointsConfig.PointGroupEntry? group;
        if (string.IsNullOrWhiteSpace(groupId)
            || string.Equals(groupId, "DEFAULT", StringComparison.OrdinalIgnoreCase))
        {
            group = _allGroups.FirstOrDefault(item =>
                string.Equals(item.DeviceId, GetDeviceIdentity(device), StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            group = _allGroups.FirstOrDefault(item =>
                string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase));
        }

        if (group is not null
            && !string.Equals(group.DeviceId, GetDeviceIdentity(device), StringComparison.OrdinalIgnoreCase))
            return null;
        return group is null ? null : (device, group);
    }

    private string ResolveCanonicalGroupId(string? groupId, string? deviceId)
    {
        var device = FindConfiguredDevice(deviceId);
        return ResolveGroupIdentifier(groupId, device is null ? deviceId : GetDeviceIdentity(device));
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

    private static string GetDeviceIdentityFromChoice(DevicePointDeviceChoice device)
        => !string.IsNullOrWhiteSpace(device.Id) ? device.Id.Trim() : device.Code.Trim();

    /// <summary>
    /// 右键移动和剪切粘贴共用的稳定身份移动实现。
    /// </summary>
    private Task<OperationFeedback> MovePointToGroupAsync(
        string sourcePointId,
        string sourceDeviceId,
        string targetDeviceId,
        string targetGroupId,
        bool clearCutClipboard,
        string successText,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePointId)
            || string.IsNullOrWhiteSpace(sourceDeviceId)
            || string.IsNullOrWhiteSpace(targetDeviceId)
            || string.IsNullOrWhiteSpace(targetGroupId))
            return Task.FromResult(SetFeedback(false, "点位移动所需的源或目标身份不完整"));

        var targetDevice = FindConfiguredDevice(targetDeviceId);
        if (targetDevice is null)
            return Task.FromResult(SetFeedback(false, "目标设备已不存在，点位未移动"));
        var targetDeviceIdentity = GetDeviceIdentity(targetDevice);
        var targetGroup = _allGroups.FirstOrDefault(group =>
            string.Equals(group.Id, targetGroupId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(group.DeviceId, targetDeviceIdentity, StringComparison.OrdinalIgnoreCase));
        if (targetGroup is null)
            return Task.FromResult(SetFeedback(false, "目标分组已不存在或不属于目标设备，点位未移动"));

        return RunMutationAsync(async token =>
        {
            var entries = _allEntries.Select(ClonePoint).ToList();
            var index = entries.FindIndex(point =>
                string.Equals(point.Id, sourcePointId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new DomainException("源点位已不存在，点位未移动");

            var source = entries[index];
            if (!string.Equals(ResolveDeviceId(source), sourceDeviceId, StringComparison.OrdinalIgnoreCase))
                throw new DomainException("源点位设备归属已变化，点位未移动");
            var sourceGroupId = ResolveCanonicalGroupId(source.GroupId, sourceDeviceId);
            if (string.Equals(sourceGroupId, targetGroup.Id, StringComparison.OrdinalIgnoreCase))
                return "点位已在该分组，未生成新配置";

            source.GroupId = targetGroup.Id;
            source.GroupCode = targetGroup.Code;
            var message = await SaveEntriesCoreAsync(
                entries,
                successText,
                _allGroups,
                new UiRestoreIntent(
                    DevicePointTreeNodeViewModel.CreateNodeKey(
                        DevicePointTreeNodeKind.Group,
                        targetGroup.Id,
                        targetDeviceIdentity),
                    sourcePointId),
                token);
            if (clearCutClipboard)
            {
                _pointClipboard = null;
                NotifyOperationStateChanged();
            }
            return message;
        }, ct);
    }
}
