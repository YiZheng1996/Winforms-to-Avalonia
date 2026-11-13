using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 单点只读诊断行。
/// </summary>
public sealed partial class DevicePointDiagnosticRow : ObservableObject
{
    public required string PointId { get; init; }
    public required string DeviceId { get; init; }
    public required string DeviceText { get; init; }
    public required string ModbusStationText { get; init; }
    public bool HasModbusStation => !string.IsNullOrWhiteSpace(ModbusStationText);
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
    private bool _isClosed;

    public DevicePointDiagnosticsViewModel(
        ShellServices services,
        string scopeName,
        IReadOnlyList<DevicePointDiagnosticTarget> targets)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        ScopeName = scopeName;
        var devices = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();
        foreach (var target in _targets)
        {
            var device = DeviceStationDisplay.ResolveDevice(devices, target.DeviceId);
            Points.Add(new DevicePointDiagnosticRow
            {
                PointId = target.PointId,
                DeviceId = target.DeviceId,
                DeviceText = DeviceStationDisplay.FormatDeviceText(device),
                ModbusStationText = DeviceStationDisplay.FormatLabel(device),
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

    public bool CanReadSelected => !_isClosed && !IsBusy && SelectedPoint is not null;
    public bool CanReadScope => !_isClosed && !IsBusy && Points.Count > 0;

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
        if (_isClosed)
            return Task.FromResult(new OperationFeedback(false, "诊断窗口已关闭"));
        if (IsBusy)
            return Task.FromResult(new OperationFeedback(false, "已有诊断读取正在执行"));
        if (SelectedPoint is null)
            return Task.FromResult(new OperationFeedback(false, "请先选择一个点位"));
        return RunReadAsync(new[] { SelectedPoint }, ct);
    }

    public Task<OperationFeedback> ReadScopeAsync(CancellationToken ct = default)
    {
        if (_isClosed)
            return Task.FromResult(new OperationFeedback(false, "诊断窗口已关闭"));
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

    /// <summary>
    /// 标记窗口已关闭并取消所有尚未完成的读取。即使底层忽略取消，返回结果也不会再写入行。
    /// </summary>
    public void Close()
    {
        if (_isClosed)
            return;
        _isClosed = true;
        CancelCurrentRead();
        OnPropertyChanged(nameof(CanReadSelected));
        OnPropertyChanged(nameof(CanReadScope));
    }

    private async Task<OperationFeedback> RunReadAsync(
        IReadOnlyList<DevicePointDiagnosticRow> rows,
        CancellationToken ct)
    {
        if (_isClosed)
            return new OperationFeedback(false, "诊断窗口已关闭");
        if (_services.DeviceModes.Runtime is null)
            return SetFeedback(false, "设备运行时未初始化，不能读取点位");
        var reader = ResolveDiagnosticReader();
        if (reader is null)
            return SetFeedback(false, "诊断读取服务未配置");

        var entered = false;
        try
        {
            entered = await _readGate.WaitAsync(0, ct);
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

            var result = await reader.ReadAsync(
                rows.Select(row => new XXX.TestBench.Core.Application.DevicePointDiagnosticTarget(
                    row.PointId,
                    row.DeviceId,
                    row.PointName,
                    row.Address,
                    row.DataTypeText,
                row.ChannelId)).ToList(),
                token);
            if (_isClosed)
                return new OperationFeedback(false, "诊断窗口已关闭，读取结果已丢弃");
            ApplyResult(rows, result);
            CompletedCount = result.Items.Count(item =>
                item.Kind != DevicePointDiagnosticResultKind.Cancelled);
            var failed = result.FailedCount + result.DiscardedCount + result.CancelledCount;
            var message = $"诊断完成：正常 {result.GoodCount} 个，陈旧 {result.StaleCount} 个，无效 {result.BadCount} 个，失败 {result.FailedCount} 个，丢弃 {result.DiscardedCount} 个，取消 {result.CancelledCount} 个";
            return SetFeedback(
                failed == 0 && result.StaleCount == 0 && result.BadCount == 0,
                message);
        }
        catch (OperationCanceledException)
        {
            if (_isClosed)
                return new OperationFeedback(false, "诊断窗口已关闭，读取结果已丢弃");
            return SetFeedback(false, "诊断读取已取消，未完成点位保持原状态");
        }
        catch (DomainException ex)
        {
            return SetFeedback(false, ex.Message);
        }
        catch (Exception ex)
        {
            return SetFeedback(false, string.IsNullOrWhiteSpace(ex.Message)
                ? "诊断读取失败，请查看应用日志"
                : ex.Message);
        }
        finally
        {
            if (entered)
            {
                IsBusy = false;
                _currentRead?.Dispose();
                _currentRead = null;
                _readGate.Release();
            }
        }
    }

    private void ApplyResult(
        IReadOnlyList<DevicePointDiagnosticRow> rows,
        DevicePointDiagnosticReadResult result)
    {
        var rowsByPointId = rows
            .GroupBy(row => row.PointId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new Queue<DevicePointDiagnosticRow>(group), StringComparer.OrdinalIgnoreCase);
        foreach (var item in result.Items)
        {
            if (!rowsByPointId.TryGetValue(item.Target.PointId, out var queue)
                || queue.Count == 0)
                continue;
            var row = queue.Dequeue();
            row.ErrorText = item.Error ?? string.Empty;
            if (item.Value is { } value)
            {
                row.ValueText = FormatValue(value.Value);
                row.QualityText = FormatQuality(value.Quality);
                row.TimestampText = value.TimestampUtc == default
                    ? "—"
                    : value.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
            }
            (row.StatusText, row.QualityText) = item.Kind switch
            {
                DevicePointDiagnosticResultKind.Good => ("成功", item.Value is null ? "正常" : row.QualityText),
                DevicePointDiagnosticResultKind.Stale => ("质量陈旧", "数据陈旧"),
                DevicePointDiagnosticResultKind.Bad => ("质量无效", "无效"),
                DevicePointDiagnosticResultKind.Discarded => ("已丢弃", "未读取"),
                DevicePointDiagnosticResultKind.Cancelled => ("已取消", "未读取"),
                _ => ("读取失败", "未知")
            };
        }
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
