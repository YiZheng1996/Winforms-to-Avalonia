using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointManagementViewModel
{
    public bool CanExportCurrentRange => !IsBusy
        && IsGroupedConfiguration
        && CreateCatalogExportRequest() is not null;

    /// <summary>
    /// 当前范围导出请求。范围跟随左树，空范围或虚拟节点返回 null。
    /// </summary>
    public DevicePointCatalogExportRequest? CreateCatalogExportRequest()
    {
        if (!IsGroupedConfiguration || SelectedTreeNode is null)
            return null;

        var node = SelectedTreeNode;
        IReadOnlyList<PointsConfig.PointEntry> points;
        IReadOnlyList<DeviceConfig.DeviceEntry> devices;
        switch (node.Kind)
        {
            case DevicePointTreeNodeKind.Root:
                points = _allEntries.Select(ClonePoint).ToList();
                devices = GetDevicesForPoints(points);
                break;
            case DevicePointTreeNodeKind.Channel when !node.IsVirtual:
                var channelDevices = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
                    .Where(device => string.Equals(device.ChannelId, node.Id, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(device.ChannelId, node.Code, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var channelDeviceIds = channelDevices.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                points = _allEntries
                    .Where(point => channelDeviceIds.Contains(ResolveDeviceId(point)))
                    .Select(ClonePoint)
                    .ToList();
                devices = channelDevices;
                break;
            case DevicePointTreeNodeKind.Device when !node.IsOrphan:
                devices = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
                    .Where(device => string.Equals(device.Id, node.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                points = _allEntries
                    .Where(point => string.Equals(ResolveDeviceId(point), node.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(ClonePoint)
                    .ToList();
                break;
            case DevicePointTreeNodeKind.Group when !node.IsVirtual:
                devices = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
                    .Where(device => string.Equals(device.Id, node.OwnerDeviceId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                points = _allEntries
                    .Where(point => string.Equals(ResolveDeviceId(point), node.OwnerDeviceId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(point.GroupId, node.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(ClonePoint)
                    .ToList();
                break;
            default:
                return null;
        }

        if (points.Count == 0)
            return null;
        var deviceIds = devices.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = _allGroups
            .Where(group => deviceIds.Contains(group.DeviceId))
            .Select(CloneGroup)
            .ToList();
        return new DevicePointCatalogExportRequest(
            GetCurrentTemplateScopeName(),
            points,
            groups,
            devices);
    }

    /// <summary>
    /// 导出当前树范围真实点位，供批量维护后重新导入。
    /// </summary>
    public Task<OperationFeedback> ExportCurrentRangeAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.FromResult(SetFeedback(false, "请选择导出文件路径"));
        var request = CreateCatalogExportRequest();
        if (request is null)
            return Task.FromResult(SetFeedback(false, "当前范围没有可导出的点位"));

        return RunMutationAsync(async token =>
        {
            await _services.DevicePointCatalogExporter.ExportAsync(filePath, request, token);
            return $"已导出当前范围 {request.Points.Count} 个点位：{Path.GetFileName(filePath)}";
        }, ct);
    }

    public string CreateCatalogExportFileName()
    {
        var scope = GetCurrentTemplateScopeName();
        var safe = string.Concat(scope.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return $"设备点位_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    }


    /// <summary>
    /// 下载空白填写模板；与当前范围真实数据导出分开。
    /// </summary>
    public Task<OperationFeedback> DownloadTemplateAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.FromResult(SetFeedback(false, "请选择模板保存路径"));
        if (TemplateScopeOptions.Count == 0)
            return DownloadTemplateCoreAsync(filePath, ct);
        var request = CreateTemplateExportRequest();
        if (request is null)
            return Task.FromResult(SetFeedback(false, "当前没有可用设备范围，请先配置设备后再下载模板"));
        return RunMutationAsync(async token =>
        {
            await _services.DevicePointTemplateExporter.ExportAsync(filePath, request, token);
            return $"已按“{request.ScopeDisplayName}”生成设备点位模板：{Path.GetFileName(filePath)}";
        }, ct);
    }

    private async Task<OperationFeedback> DownloadTemplateCoreAsync(
        string filePath,
        CancellationToken ct)
    {
        await LoadAsync(ct);
        var request = CreateTemplateExportRequest();
        if (request is null)
            return SetFeedback(false, "当前没有可用设备范围，请先配置设备后再下载模板");
        return await RunMutationAsync(async token =>
        {
            await _services.DevicePointTemplateExporter.ExportAsync(filePath, request, token);
            return $"已按“{request.ScopeDisplayName}”生成设备点位模板：{Path.GetFileName(filePath)}";
        }, ct);
    }

    private IReadOnlyList<DeviceConfig.DeviceEntry> GetDevicesForPoints(
        IReadOnlyList<PointsConfig.PointEntry> points)
    {
        var ids = points
            .Select(ResolveDeviceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => ids.Contains(device.Id))
            .ToList();
    }

}
