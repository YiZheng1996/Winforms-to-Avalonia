using Avalonia.Threading;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointManagementViewModel
{
    private bool _isObservingRuntime;

    public bool CanOpenDiagnostics => !IsBusy
        && _services.DeviceModes.Runtime is not null
        && GetDiagnosticsEntries().Count > 0;

    /// <summary>
    /// 创建只读诊断窗口模型；运行时引用在窗口模型内部按每次读取重新捕获并校验。
    /// </summary>
    public DevicePointDiagnosticsViewModel? CreateDiagnosticsViewModel()
    {
        var entries = GetDiagnosticsEntries();
        if (entries.Count == 0)
            return null;

        var targets = entries.Select(CreateDiagnosticTarget).ToList();
        return new DevicePointDiagnosticsViewModel(
            _services,
            GetDiagnosticsScopeName(),
            targets);
    }


    /// <summary>
    /// 从表格行创建单点诊断模型，不改变左侧树选择。
    /// </summary>
    public DevicePointDiagnosticsViewModel? CreateDiagnosticsViewModelForPoint(DevicePointRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_services.DeviceModes.Runtime?.GetPoint(row.PointId) is null)
            return null;
        return new DevicePointDiagnosticsViewModel(
            _services,
            $"点位：{row.PointName}",
            new[] { CreateDiagnosticTarget(ClonePoint(row.Entry)) });
    }

    /// <summary>
    /// 页面显示时订阅设备运行状态；只刷新摘要，不重建树或清空点位清单。
    /// </summary>
    public void ActivateRuntimeObservation()
    {
        if (_isObservingRuntime)
            return;
        _services.DeviceModes.StateChanged += OnDeviceRuntimeStateChanged;
        EventLog.Attach();
        _isObservingRuntime = true;
        RefreshRuntimeObservation();
    }

    public void DeactivateRuntimeObservation()
    {
        if (!_isObservingRuntime)
            return;
        _services.DeviceModes.StateChanged -= OnDeviceRuntimeStateChanged;
        EventLog.Detach();
        _isObservingRuntime = false;
    }

    private void OnDeviceRuntimeStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshRuntimeObservation();
        else
            Dispatcher.UIThread.Post(RefreshRuntimeObservation);
    }

    private void RefreshRuntimeObservation()
    {
        OnPropertyChanged(nameof(ActiveRevisionText));
        OnPropertyChanged(nameof(RuntimeStatusText));
        OnPropertyChanged(nameof(CanOpenDiagnostics));
        OnPropertyChanged(nameof(CanTestSelectedConnection));
        NotifyScopeDetails();
        RefreshCachedValues();
    }

    private IReadOnlyList<PointsConfig.PointEntry> GetDiagnosticsEntries()
    {
        var node = SelectedTreeNode;
        if (node is null)
            return Array.Empty<PointsConfig.PointEntry>();
        return node.Kind switch
        {
            DevicePointTreeNodeKind.Root => _allEntries.Select(ClonePoint).ToList(),
            DevicePointTreeNodeKind.Channel when !node.IsVirtual => _allEntries
                .Where(point => string.Equals(
                    ResolveDeviceChannelId(ResolveDeviceId(point)),
                    node.Id,
                    StringComparison.OrdinalIgnoreCase))
                .Select(ClonePoint)
                .ToList(),
            DevicePointTreeNodeKind.Device when !node.IsOrphan => _allEntries
                .Where(point => string.Equals(ResolveDeviceId(point), node.Id, StringComparison.OrdinalIgnoreCase))
                .Select(ClonePoint)
                .ToList(),
            DevicePointTreeNodeKind.Group when !node.IsVirtual => _allEntries
                .Where(point => string.Equals(ResolveDeviceId(point), node.OwnerDeviceId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(point.GroupId, node.Id, StringComparison.OrdinalIgnoreCase))
                .Select(ClonePoint)
                .ToList(),
            _ => Array.Empty<PointsConfig.PointEntry>()
        };
    }

    private DevicePointDiagnosticTarget CreateDiagnosticTarget(PointsConfig.PointEntry point)
    {
        var deviceId = ResolveDeviceId(point);
        return new DevicePointDiagnosticTarget(
            point.Id,
            deviceId,
            string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name,
            point.Address,
            DevicePointTypeCatalog.ToDisplayName(point.RawDataTypeKind, point.RawDataType),
            ResolveDeviceChannelId(deviceId));
    }

    private string ResolveDeviceChannelId(string deviceId)
    {
        var device = FindConfiguredDevice(deviceId);
        if (device is null)
            return string.Empty;
        var channel = ResolveConfiguredChannel(
            device,
            (_services.DeviceConfig.Channels ?? new List<ChannelEntry>()).Where(item => item is not null).ToList());
        return channel?.Id?.Trim() ?? string.Empty;
    }

    private string GetDiagnosticsScopeName()
        => SelectedTreeNode switch
        {
            null => "当前范围",
            { Kind: DevicePointTreeNodeKind.Root } => "全部设备",
            { Kind: DevicePointTreeNodeKind.Channel } node => $"通道：{node.DisplayText}",
            { Kind: DevicePointTreeNodeKind.Device } node => $"设备：{node.DisplayText}",
            { Kind: DevicePointTreeNodeKind.Group } node => $"分组：{node.DisplayText}",
            _ => "当前范围"
        };
}
