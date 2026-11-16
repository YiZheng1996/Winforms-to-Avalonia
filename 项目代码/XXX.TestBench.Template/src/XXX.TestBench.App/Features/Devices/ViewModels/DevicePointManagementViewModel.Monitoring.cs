using Avalonia.Threading;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointManagementViewModel
{
    private DispatcherTimer? _liveMonitorTimer;
    private long _liveMonitorGeneration;
    private bool _liveMonitorAttached;

    /// <summary>
    /// 实时监视刷新周期。计时器只读取运行时缓存，不触发任何设备读取。
    /// </summary>
    public int LiveMonitorIntervalMs => 500;

    public bool IsLiveMonitorTimerRunning => _liveMonitorTimer?.IsEnabled == true;

    partial void OnWorkspaceModeChanged(DevicePointWorkspaceMode value)
    {
        UpdateLiveMonitorLifecycle();
        if (IsLiveMonitorMode)
            RefreshCachedValuesCore();
        NotifyScopeDetails();
    }

    private void UpdateLiveMonitorLifecycle()
    {
        if (!_liveMonitorAttached || !IsLiveMonitorMode)
        {
            StopLiveMonitorTimer();
            return;
        }

        if (_liveMonitorTimer is null)
        {
            _liveMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LiveMonitorIntervalMs)
            };
            _liveMonitorTimer.Tick += OnLiveMonitorTimerTick;
        }

        _liveMonitorGeneration++;
        _liveMonitorTimer.Start();
    }

    private void StopLiveMonitorTimer()
    {
        _liveMonitorGeneration++;
        _liveMonitorTimer?.Stop();
    }

    private void OnLiveMonitorTimerTick(object? sender, EventArgs e)
    {
        if (!_liveMonitorAttached || !IsLiveMonitorMode)
            return;
        var generation = _liveMonitorGeneration;
        if (Dispatcher.UIThread.CheckAccess())
            RefreshCachedValuesIfCurrent(generation);
        else
            Dispatcher.UIThread.Post(
                () => RefreshCachedValuesIfCurrent(generation),
                DispatcherPriority.Background);
    }

    private void RefreshCachedValuesIfCurrent(long generation)
    {
        if (generation != _liveMonitorGeneration
            || !_liveMonitorAttached
            || !IsLiveMonitorMode)
            return;
        RefreshCachedValuesCore();
    }

    private void RefreshCachedValuesCore()
    {
        var runtime = _services.DeviceModes.Runtime;
        var revision = runtime?.ActiveRevision ?? string.Empty;
        foreach (var row in Points)
        {
            if (runtime is not null
                && runtime.TryGetCachedValue(row.PointId, out var value)
                && IsCompatibleCachedValue(runtime, revision, row, value))
                row.UpdateCachedValue(value);
            else
                row.UpdateCachedValue(null);
        }
    }

    private bool IsCompatibleCachedValue(
        IDeviceRuntime runtime,
        string revision,
        DevicePointRow row,
        PointValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.PointId)
            && !string.Equals(value.PointId, row.PointId, StringComparison.OrdinalIgnoreCase))
            return false;
        var deviceId = ResolveDeviceId(row.Entry);
        if (!string.IsNullOrWhiteSpace(value.DeviceId)
            && !string.Equals(value.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(value.Revision)
            && !string.IsNullOrWhiteSpace(revision)
            && !string.Equals(value.Revision, revision, StringComparison.Ordinal))
            return false;

        var generation = GetCachedDeviceGeneration(runtime, deviceId);
        if (generation > 0 && value.ConnectionGeneration > 0 && generation != value.ConnectionGeneration)
            return false;

        return true;
    }

    private static long GetCachedDeviceGeneration(IDeviceRuntime runtime, string deviceId)
    {
        try
        {
            return runtime.GetDeviceStatus(deviceId).ConnectionGeneration;
        }
        catch (DomainException)
        {
            return runtime.Status.ConnectionGeneration;
        }
    }

    private void StartLiveMonitorTimer()
    {
        _liveMonitorAttached = true;
        UpdateLiveMonitorLifecycle();
        if (IsLiveMonitorMode)
            RefreshCachedValuesCore();
    }

    private void StopLiveMonitorObservation()
    {
        _liveMonitorAttached = false;
        StopLiveMonitorTimer();
    }

    public void RefreshLiveMonitorNow()
    {
        if (IsLiveMonitorMode)
            RefreshCachedValuesCore();
    }
}
