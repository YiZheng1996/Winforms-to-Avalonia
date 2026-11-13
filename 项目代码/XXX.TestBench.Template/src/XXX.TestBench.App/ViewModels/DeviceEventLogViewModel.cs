using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备事件日志的级别筛选。
/// </summary>
public enum DeviceEventLogFilter
{
    All,
    Information,
    Warning,
    Error
}

/// <summary>
/// 事件日志级别的客户可读选项。
/// </summary>
public sealed record DeviceEventLogFilterChoice(
    DeviceEventLogFilter Value,
    string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 设备通信事件的页面状态。事件来自内存 EventHub，不查询审计库或文本日志。
/// </summary>
public sealed partial class DeviceEventLogViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceEventSink? _eventSink;
    private bool _disposed;
    private bool _subscribed;
    private string? _channelId;
    private string? _deviceId;

    public DeviceEventLogViewModel(IDeviceEventSink? eventSink)
    {
        _eventSink = eventSink;
        FilterOptions =
        [
            new(DeviceEventLogFilter.All, "全部"),
            new(DeviceEventLogFilter.Information, "信息"),
            new(DeviceEventLogFilter.Warning, "警告"),
            new(DeviceEventLogFilter.Error, "错误")
        ];
        SelectedFilter = FilterOptions[0];
        Attach();
    }

    public ObservableCollection<DeviceEventLogFilterChoice> FilterOptions { get; }
    public ObservableCollection<DeviceCommunicationEvent> VisibleEvents { get; } = new();
    public ObservableCollection<DeviceCommunicationEvent> Events => VisibleEvents;
    public ObservableCollection<DeviceCommunicationEvent> LogEntries => VisibleEvents;

    [ObservableProperty]
    private DeviceEventLogFilterChoice? _selectedFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    private bool _isExpanded = true;

    public bool HasEvents => VisibleEvents.Count > 0;
    public string ToggleText => IsExpanded ? "收起" : "展开";
    public string ScopeText
        => string.IsNullOrWhiteSpace(_deviceId) && string.IsNullOrWhiteSpace(_channelId)
            ? "全部范围"
            : !string.IsNullOrWhiteSpace(_deviceId) ? "当前设备" : "当前通道";

    partial void OnSelectedFilterChanged(DeviceEventLogFilterChoice? value)
    {
        Rebuild();
        OnPropertyChanged(nameof(HasEvents));
    }

    /// <summary>
    /// 设置当前树范围。空值表示不按范围过滤。
    /// </summary>
    public void SetScope(string? channelId, string? deviceId)
    {
        _channelId = Normalize(channelId);
        _deviceId = Normalize(deviceId);
        Rebuild();
        OnPropertyChanged(nameof(ScopeText));
    }

    public void SetFilter(DeviceEventLogFilter filter)
    {
        SelectedFilter = FilterOptions.FirstOrDefault(option => option.Value == filter)
            ?? FilterOptions[0];
    }

    public void Refresh() => Rebuild();

    public void Attach()
    {
        if (_disposed || _subscribed || _eventSink is null) return;
        _eventSink.EventReceived += OnEventReceived;
        _subscribed = true;
        Rebuild();
    }

    public void Detach()
    {
        if (!_subscribed || _eventSink is null) return;
        _eventSink.EventReceived -= OnEventReceived;
        _subscribed = false;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void ToggleExpanded() => IsExpanded = !IsExpanded;

    private void OnEventReceived(object? sender, DeviceCommunicationEvent value)
    {
        if (_disposed) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            Rebuild();
        });
    }

    private void Rebuild()
    {
        if (_disposed) return;
        var filter = SelectedFilter?.Value ?? DeviceEventLogFilter.All;
        var snapshot = _eventSink?.Snapshot(200) ?? Array.Empty<DeviceCommunicationEvent>();
        var filtered = snapshot
            .Where(item => MatchesSeverity(item, filter))
            .Where(item => string.IsNullOrWhiteSpace(_deviceId)
                || string.Equals(item.DeviceId, _deviceId, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(_channelId)
                || string.Equals(item.ChannelId, _channelId, StringComparison.OrdinalIgnoreCase))
            .Take(200)
            .ToList();

        VisibleEvents.Clear();
        foreach (var item in filtered)
            VisibleEvents.Add(item);
        OnPropertyChanged(nameof(HasEvents));
    }

    private static bool MatchesSeverity(
        DeviceCommunicationEvent value,
        DeviceEventLogFilter filter)
        => filter switch
        {
            DeviceEventLogFilter.Information => value.Severity == DeviceEventSeverity.Information,
            DeviceEventLogFilter.Warning => value.Severity == DeviceEventSeverity.Warning,
            DeviceEventLogFilter.Error => value.Severity == DeviceEventSeverity.Error,
            _ => true
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }
}
