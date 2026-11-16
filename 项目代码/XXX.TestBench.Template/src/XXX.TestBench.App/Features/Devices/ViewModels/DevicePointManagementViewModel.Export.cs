using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointManagementViewModel
{
    public bool CanExportCurrentRange
    {
        get
        {
            return CanStartPageOperation
                && IsGroupedConfiguration
                && SelectedTreeNode is not { IsVirtual: true, IsOrphan: true }
                && _currentScope.Points.Count > 0;
        }
    }

    /// <summary>
    /// 当前范围导出请求。范围跟随左树，空范围或虚拟节点返回 null。
    /// </summary>
    public DevicePointCatalogExportRequest? CreateCatalogExportRequest()
    {
        RefreshCurrentScopeSnapshot();
        if (!IsGroupedConfiguration || SelectedTreeNode is null || !_currentScope.Points.Any())
            return null;

        // 只构造当前范围的只读请求；真正写文件时导出器消费快照，不重新推断范围。
        var points = _currentScope.Points;
        var devices = _currentScope.Devices;
        var deviceIds = devices.Select(GetDeviceIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = _currentScope.Groups
            .Where(group => deviceIds.Contains(group.DeviceId))
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

        return RunMutationAsync(async token =>
        {
            var request = CreateCatalogExportRequest();
            if (request is null)
                throw new Core.Common.DomainException("当前范围没有可导出的点位");
            await _services.DevicePointCatalogExporter.ExportAsync(filePath, request, token);
            return $"已导出当前范围 {request.Points.Count} 个点位：{Path.GetFileName(filePath)}";
        }, ct: ct);
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
        return RunPageOperationAsync(async token =>
        {
            await LoadCoreAsync(token);
            var request = CreateTemplateExportRequest();
            if (request is null)
                throw new Core.Common.DomainException("当前没有可用设备范围，请先配置设备后再下载模板");
            await _services.DevicePointTemplateExporter.ExportAsync(filePath, request, token);
            return $"已按“{request.ScopeDisplayName}”生成设备点位模板：{Path.GetFileName(filePath)}";
        }, ct: ct);
    }

}
