using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class ProcessMonitorViewModel
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _observationCts;
    private int _pageGeneration;
    private int _refreshing;
    private bool _isRuntimeObservationActive;

    public bool IsSimulationPage => _services.DeviceModes.Runtime?.IsSimulation == true;

    /// <summary>
    /// 由页面生命周期调用；只订阅一次设备运行时变化。
    /// </summary>
    public void ActivateRuntimeObservation()
    {
        if (_isRuntimeObservationActive)
            return;
        _isRuntimeObservationActive = true;
        _observationCts = new CancellationTokenSource();
        _services.DeviceModes.StateChanged += OnDeviceModeStateChanged;
        _ = LoadAsync(_observationCts.Token);
    }

    /// <summary>
    /// 页面卸载、注销或设备运行时替换时取消页面工作；不释放共享运行时。
    /// </summary>
    public void DeactivateRuntimeObservation()
    {
        if (!_isRuntimeObservationActive)
            return;
        _isRuntimeObservationActive = false;
        _services.DeviceModes.StateChanged -= OnDeviceModeStateChanged;
        _observationCts?.Cancel();
        _observationCts?.Dispose();
        _observationCts = null;
        Interlocked.Increment(ref _pageGeneration);
    }

    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        await _loadGate.WaitAsync(ct);
        var generation = Volatile.Read(ref _pageGeneration);
        IsBusy = true;
        try
        {
            var runtime = _services.DeviceModes.Runtime;
            if (runtime is null)
            {
                ResetProcessBindings();
                Points.Clear();
                StatusMessage = "设备运行时未初始化";
                return;
            }

            var points = await runtime.ListPointsAsync(ct);
            if (!IsCurrent(generation, runtime))
                return;

            Points.Clear();
            foreach (var point in points)
                Points.Add(new PointRow
                {
                    Code = point.Code,
                    Address = point.Address,
                    PointId = point.PointId,
                    DeviceId = point.DeviceId,
                    IsWritable = point.IsWritable,
                    RiskLevel = point.RiskLevel
                });

            ApplyProcessBindings(runtime, points);
            await RefreshCoreAsync(runtime, generation, ct);
            StatusMessage = "工艺监控已加载";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _loadGate.Release();
        }
    }

    /// <summary>
    /// 只读取设备运行时缓存；硬件轮询由运行时自身负责。
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => RefreshAsync(ct));
            return;
        }
        if (Interlocked.Exchange(ref _refreshing, 1) != 0)
            return;
        try
        {
            var runtime = _services.DeviceModes.Runtime;
            if (runtime is null)
            {
                ResetProcessSamples();
                OnPropertyChanged(nameof(RuntimeStatusText));
                return;
            }
            await RefreshCoreAsync(runtime, Volatile.Read(ref _pageGeneration), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private Task RefreshCoreAsync(
        IDeviceRuntime runtime,
        int generation,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var revision = runtime.ActiveRevision ?? string.Empty;
        var samples = _snapshotReader.Capture(runtime, DateTime.UtcNow);
        if (!IsCurrent(generation, runtime)
            || !string.Equals(revision, runtime.ActiveRevision, StringComparison.Ordinal))
        {
            ResetProcessSamples();
            StatusMessage = "设备配置正在切换，等待新版本";
            return Task.CompletedTask;
        }

        foreach (var item in _processPoints)
        {
            if (samples.TryGetValue(item.Key, out var sample))
                item.Value.ApplySample(sample);
        }

        if (samples.TryGetValue(ProcessSignalCatalog.PressureSetpointReadback, out var readback))
            PressureSetpoint.ApplyReadback(readback);
        Diagram.UpdatePressureStates();
        RefreshLegacyPointRows(runtime);
        OnPropertyChanged(nameof(IsSimulationPage));
        OnPropertyChanged(nameof(RuntimeStatusText));
        return Task.CompletedTask;
    }

    private bool IsCurrent(int generation, IDeviceRuntime runtime)
        => generation == Volatile.Read(ref _pageGeneration)
            && ReferenceEquals(runtime, _services.DeviceModes.Runtime);

    private void OnDeviceModeStateChanged(object? sender, EventArgs e)
    {
        if (!_isRuntimeObservationActive)
            return;
        var token = _observationCts?.Token ?? CancellationToken.None;
        Dispatcher.UIThread.Post(() => _ = LoadAsync(token), DispatcherPriority.Background);
    }

    private void ResetProcessBindings()
    {
        foreach (var point in _processPoints.Values)
            point.ApplyBinding(null, null, false);
        Diagram.ResetPressureStates();
        OnPropertyChanged(nameof(IsSimulationPage));
        OnPropertyChanged(nameof(RuntimeStatusText));
    }

    private void ResetProcessSamples()
    {
        foreach (var point in _processPoints.Values)
            point.ResetForRuntimeChange();
        PressureSetpoint.ApplyReadback(new Core.Application.ProcessSample(
            ProcessSignalCatalog.PressureSetpointReadback,
            null,
            null,
            Core.Application.ProcessDataState.Waiting,
            null,
            string.Empty,
            0));
        Diagram.ResetPressureStates();
    }

    private static void RefreshLegacyPointRows(IDeviceRuntime runtime, IEnumerable<PointRow> rows)
    {
        foreach (var row in rows)
        {
            if (runtime.TryGetCachedValue(row.PointId, out var value))
            {
                row.Value = value.Value?.ToString() ?? string.Empty;
                row.Quality = value.Quality switch
                {
                    PointQuality.Good => "正常",
                    PointQuality.Stale => "陈旧",
                    PointQuality.Bad => "无效",
                    _ => "未读取"
                };
            }
            else
            {
                row.Value = string.Empty;
                row.Quality = "未读取";
            }
        }
    }

    private void RefreshLegacyPointRows(IDeviceRuntime runtime)
        => RefreshLegacyPointRows(runtime, Points);
}
