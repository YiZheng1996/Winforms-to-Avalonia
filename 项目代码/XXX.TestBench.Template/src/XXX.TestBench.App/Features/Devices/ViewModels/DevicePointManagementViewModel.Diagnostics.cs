using Avalonia.Threading;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointManagementViewModel
{
    private bool _isObservingRuntime;
    private string? _lastActiveRevisionText;
    private string? _lastRuntimeStatusText;
    private string? _lastScopeDetailsStatusText;

    public bool CanOpenDiagnostics => CanStartPageOperation
        && _currentScope.CanDiagnose;

    /// <summary>
    /// 创建只读诊断窗口模型；运行时引用在窗口模型内部按每次读取重新捕获并校验。
    /// </summary>
    public DevicePointDiagnosticsViewModel? CreateDiagnosticsViewModel()
    {
        RefreshCurrentScopeSnapshot();
        var entries = _currentScope.Points;
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
        if (_services.DeviceModes.Runtime is null || string.IsNullOrWhiteSpace(row.PointId))
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
        StartLiveMonitorTimer();
        RefreshRuntimeObservation();
    }

    public void DeactivateRuntimeObservation()
    {
        if (!_isObservingRuntime)
            return;
        _services.DeviceModes.StateChanged -= OnDeviceRuntimeStateChanged;
        EventLog.Detach();
        _isObservingRuntime = false;
        StopLiveMonitorObservation();
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
        RefreshCurrentScopeSnapshot();
        NotifyRuntimeSummaryChanges();
        OnPropertyChanged(nameof(CanOpenDiagnostics));
        OnPropertyChanged(nameof(CanTestSelectedConnection));
        NotifyScopeDetails();
        RefreshCachedValues();
    }

    private void NotifyRuntimeSummaryChanges()
    {
        var activeRevisionText = ActiveRevisionText;
        if (!string.Equals(_lastActiveRevisionText, activeRevisionText, StringComparison.Ordinal))
        {
            _lastActiveRevisionText = activeRevisionText;
            OnPropertyChanged(nameof(ActiveRevisionText));
        }

        var runtimeStatusText = RuntimeStatusText;
        if (!string.Equals(_lastRuntimeStatusText, runtimeStatusText, StringComparison.Ordinal))
        {
            _lastRuntimeStatusText = runtimeStatusText;
            OnPropertyChanged(nameof(RuntimeStatusText));
        }
    }

    private void NotifyScopeDetailsStatusIfChanged()
    {
        var statusText = ScopeDetailsStatusText;
        if (string.Equals(_lastScopeDetailsStatusText, statusText, StringComparison.Ordinal))
            return;

        _lastScopeDetailsStatusText = statusText;
        OnPropertyChanged(nameof(ScopeDetailsStatusText));
        OnPropertyChanged(nameof(ScopeDetailsStatusBackground));
        OnPropertyChanged(nameof(ScopeDetailsStatusForeground));
    }

    private DevicePointDiagnosticTarget CreateDiagnosticTarget(PointsConfig.PointEntry point)
    {
        var deviceId = ResolveDeviceId(point);
        return new DevicePointDiagnosticTarget(
            point.Id,
            deviceId,
            string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name,
            point.Address,
            DevicePointTypeCatalog.ToDisplayName(point.DataTypeKind),
            ResolveDeviceChannelId(deviceId));
    }

    private IDevicePointDiagnosticReader? ResolveDiagnosticReader()
    {
        return _services.DiagnosticReader
            ?? (_services.ManualOperationGuard is { } guard
                ? new DevicePointDiagnosticReader(
                    guard,
                    () => _services.DeviceModes.Runtime)
                : null);
    }

    private static string FormatDiagnosticFeedback(
        DevicePointDiagnosticReadResult result,
        int requestedCount)
        => $"已读取当前范围 {requestedCount} 个点位：正常 {result.GoodCount}，陈旧 {result.StaleCount}，无效 {result.BadCount}，失败 {result.FailedCount}，丢弃 {result.DiscardedCount}";

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
