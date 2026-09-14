using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 诊断目标只携带运行时读取需要的稳定身份，不持有配置对象的可变引用。
/// </summary>
public sealed record DevicePointDiagnosticTarget(
    string PointId,
    string DeviceId,
    string PointName,
    string Address,
    string DataTypeText,
    string ChannelId);

/// <summary>
/// 单点只读诊断行。
/// </summary>
public sealed partial class DevicePointDiagnosticRow : ObservableObject
{
    public required string PointId { get; init; }
    public required string DeviceId { get; init; }
    public required string PointName { get; init; }
    public required string Address { get; init; }
    public required string DataTypeText { get; init; }
    public required string ChannelId { get; init; }

    [ObservableProperty] private string _valueText = "未读取";
    [ObservableProperty] private string _qualityText = "未知";
    [ObservableProperty] private string _timestampText = "—";
    [ObservableProperty] private string _statusText = "未读取";
    [ObservableProperty] private string _errorText = string.Empty;
}

/// <summary>
/// 只读点位诊断。所有读取都使用打开窗口时捕获的当前运行时，旧 Revision 或旧连接代次结果会被丢弃。
/// </summary>
public sealed partial class DevicePointDiagnosticsViewModel : ObservableObject
{
    private readonly ShellServices _services;
    private readonly IReadOnlyList<DevicePointDiagnosticTarget> _targets;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private CancellationTokenSource? _currentRead;

    public DevicePointDiagnosticsViewModel(
        ShellServices services,
        string scopeName,
        IReadOnlyList<DevicePointDiagnosticTarget> targets)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        ScopeName = scopeName;
        foreach (var target in _targets)
        {
            Points.Add(new DevicePointDiagnosticRow
            {
                PointId = target.PointId,
                DeviceId = target.DeviceId,
                PointName = target.PointName,
                Address = target.Address,
                DataTypeText = target.DataTypeText,
                ChannelId = target.ChannelId
            });
        }
        TotalCount = Points.Count;
    }

    public ObservableCollection<DevicePointDiagnosticRow> Points { get; } = new();

    [ObservableProperty] private DevicePointDiagnosticRow? _selectedPoint;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _scopeName;
    [ObservableProperty] private string _runtimeSummary = "未连接";
    [ObservableProperty] private string _activeRevisionText = "未生成生效版本";
    [ObservableProperty] private string _feedbackText = string.Empty;

    public bool CanReadSelected => !IsBusy && SelectedPoint is not null;
    public bool CanReadScope => !IsBusy && Points.Count > 0;

    partial void OnSelectedPointChanged(DevicePointDiagnosticRow? value)
        => OnPropertyChanged(nameof(CanReadSelected));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanReadSelected));
        OnPropertyChanged(nameof(CanReadScope));
    }

    public Task LoadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var runtime = _services.DeviceModes.Runtime;
        RuntimeSummary = BuildRuntimeSummary(runtime);
        ActiveRevisionText = string.IsNullOrWhiteSpace(runtime?.ActiveRevision)
            ? "未生成生效版本"
            : $"当前配置：{runtime.ActiveRevision}";
        FeedbackText = runtime is null
            ? "设备运行时未初始化。"
            : runtime.IsSimulation
                ? "当前为仿真模式，以下结果不代表现场硬件正常。"
                : "当前为硬件模式，读取结果来自现有运行时连接。";
        return Task.CompletedTask;
    }

    public Task<OperationFeedback> ReadSelectedAsync(CancellationToken ct = default)
    {
        if (IsBusy)
            return Task.FromResult(new OperationFeedback(false, "已有诊断读取正在执行"));
        if (SelectedPoint is null)
            return Task.FromResult(new OperationFeedback(false, "请先选择一个点位"));
        return RunReadAsync(new[] { SelectedPoint }, ct);
    }

    public Task<OperationFeedback> ReadScopeAsync(CancellationToken ct = default)
    {
        if (IsBusy)
            return Task.FromResult(new OperationFeedback(false, "已有诊断读取正在执行"));
        return RunReadAsync(Points.ToList(), ct);
    }

    /// <summary>
    /// 取消当前读取。已取消的读取不会再更新剩余点位。
    /// </summary>
    public void CancelCurrentRead()
    {
        try
        {
            _currentRead?.Cancel();
            FeedbackText = "正在取消诊断读取...";
        }
        catch (ObjectDisposedException)
        {
            // 读取已结束，忽略取消。
        }
    }

    private async Task<OperationFeedback> RunReadAsync(
        IReadOnlyList<DevicePointDiagnosticRow> rows,
        CancellationToken ct)
    {
        var runtime = _services.DeviceModes.Runtime;
        if (runtime is null)
            return SetFeedback(false, "设备运行时未初始化，不能读取点位");

        if (await IsActiveRunAsync(ct))
            return SetFeedback(false, "试验运行中，手动读取已禁用");

        var revision = runtime.ActiveRevision ?? string.Empty;
        var entered = await _readGate.WaitAsync(0, ct);
        if (!entered)
            return SetFeedback(false, "已有诊断读取正在执行");

        _currentRead?.Dispose();
        _currentRead = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _currentRead.Token;
        IsBusy = true;
        CompletedCount = 0;
        TotalCount = rows.Count;
        foreach (var row in rows)
        {
            row.StatusText = "等待读取";
            row.ErrorText = string.Empty;
        }

        try
        {
            var success = 0;
            var failure = 0;
            var completed = 0;
            var channels = rows
                .GroupBy(row => ResolveChannelId(runtime, row.ChannelId, row.DeviceId), StringComparer.OrdinalIgnoreCase)
                .ToList();
            using var channelLimit = new SemaphoreSlim(4, 4);
            await Task.WhenAll(channels.Select(async channel =>
            {
                await channelLimit.WaitAsync(token);
                try
                {
                    foreach (var row in channel)
                    {
                        token.ThrowIfCancellationRequested();
                        if (await ReadOneAsync(row, runtime, revision, token))
                            Interlocked.Increment(ref success);
                        else
                            Interlocked.Increment(ref failure);
                        CompletedCount = Interlocked.Increment(ref completed);
                    }
                }
                finally
                {
                    channelLimit.Release();
                }
            }));

            var message = $"诊断完成：成功 {success} 个，失败/丢弃 {failure} 个";
            return SetFeedback(failure == 0, message);
        }
        catch (OperationCanceledException)
        {
            return SetFeedback(false, "诊断读取已取消，未完成点位保持原状态");
        }
        finally
        {
            IsBusy = false;
            _readGate.Release();
        }
    }

    private async Task<bool> ReadOneAsync(
        DevicePointDiagnosticRow row,
        IDeviceRuntime runtime,
        string revision,
        CancellationToken ct)
    {
        if (!ReferenceEquals(runtime, _services.DeviceModes.Runtime)
            || !string.Equals(revision, _services.DeviceModes.Runtime?.ActiveRevision, StringComparison.Ordinal))
        {
            MarkDiscarded(row, "运行配置已变化");
            return false;
        }

        var status = runtime.GetDeviceStatus(row.DeviceId);
        var generation = status.ConnectionGeneration;
        row.StatusText = "读取中";
        row.ErrorText = string.Empty;
        try
        {
            var value = await runtime.ReadFreshAsync(row.PointId, ct);
            ct.ThrowIfCancellationRequested();

            if (!ReferenceEquals(runtime, _services.DeviceModes.Runtime)
                || !string.Equals(revision, _services.DeviceModes.Runtime?.ActiveRevision, StringComparison.Ordinal))
            {
                MarkDiscarded(row, "运行配置已变化");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(value.Revision)
                && !string.IsNullOrWhiteSpace(revision)
                && !string.Equals(value.Revision, revision, StringComparison.Ordinal))
            {
                MarkDiscarded(row, "返回了旧 Revision");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(value.PointId)
                && !string.Equals(value.PointId, row.PointId, StringComparison.OrdinalIgnoreCase))
            {
                MarkDiscarded(row, "返回 PointId 不匹配");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(value.DeviceId)
                && !string.Equals(value.DeviceId, row.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                MarkDiscarded(row, "返回 DeviceId 不匹配");
                return false;
            }

            if (generation > 0 && value.ConnectionGeneration > 0 && generation != value.ConnectionGeneration)
            {
                MarkDiscarded(row, "连接代次已变化");
                return false;
            }

            row.ValueText = FormatValue(value.Value);
            row.QualityText = FormatQuality(value.Quality);
            row.TimestampText = value.TimestampUtc == default
                ? "—"
                : value.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
            row.StatusText = "成功";
            return true;
        }
        catch (OperationCanceledException)
        {
            row.StatusText = "已取消";
            row.ErrorText = "取消后不再更新";
            throw;
        }
        catch (Exception ex)
        {
            row.StatusText = "读取失败";
            row.ErrorText = ex.Message;
            return false;
        }
    }

    private static void MarkDiscarded(DevicePointDiagnosticRow row, string reason)
    {
        row.StatusText = "已丢弃";
        row.ErrorText = reason;
    }

    private async Task<bool> IsActiveRunAsync(CancellationToken ct)
        => await _services.RecordRepository.GetActiveRunningRecordAsync() is not null;

    private static string ResolveChannelId(
        IDeviceRuntime runtime,
        string channelId,
        string deviceId)
    {
        if (!string.IsNullOrWhiteSpace(channelId))
            return channelId.Trim();
        var status = runtime.GetDeviceStatus(deviceId);
        return string.IsNullOrWhiteSpace(status.ChannelId) ? deviceId : status.ChannelId.Trim();
    }

    private static string BuildRuntimeSummary(IDeviceRuntime? runtime)
    {
        if (runtime is null)
            return "未连接";
        var mode = runtime.IsSimulation
            ? "仿真模式"
            : runtime.Mode == DeviceMode.Hardware ? "硬件模式" : "混合模式";
        return $"{mode} · 状态 {FormatHealth(runtime.Status.Health)} · {runtime.Status.Name}";
    }

    private OperationFeedback SetFeedback(bool succeeded, string message)
    {
        FeedbackText = message;
        return new OperationFeedback(succeeded, message);
    }

    private static string FormatQuality(PointQuality quality)
        => quality switch
        {
            PointQuality.Good => "正常",
            PointQuality.Stale => "数据陈旧",
            PointQuality.Bad => "无效",
            _ => "未知"
        };

    private static string FormatHealth(DeviceHealth health)
        => health switch
        {
            DeviceHealth.Healthy => "正常",
            DeviceHealth.Degraded => "降级运行",
            DeviceHealth.Faulted => "故障",
            DeviceHealth.Disconnected => "已断开",
            _ => "未知"
        };

    private static string FormatValue(object? value)
        => value switch
        {
            null => "—",
            bool boolean => boolean ? "真" : "假",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "—"
        };
}
