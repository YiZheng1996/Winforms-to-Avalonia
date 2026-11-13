using System.Collections.Concurrent;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;
using XXX.TestBench.Devices.Siemens;
using XXX.TestBench.Devices.Simulation;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 单设备会话：持有独立缓存、轮询任务、重试/降级状态和协议运行时。
/// </summary>
public sealed class DeviceSession : IAsyncDisposable
{
    private readonly DeviceConfig.DeviceEntry _configuration;
    private readonly ChannelEntry _channel;
    private readonly IReadOnlyList<DevicePoint> _points;
    private readonly SimulationConfig _simulation;
    private readonly IClock _clock;
    private readonly ChannelManager _channels;
    private readonly string _revision;
    private readonly IS7PlcClientFactory? _s7ClientFactory;
    private readonly IModbusClientFactory? _modbusClientFactory;
    private readonly IDeviceEventSink? _events;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ConcurrentDictionary<string, PointValue> _cache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private IDeviceRuntime? _runtime;
    private DeviceConnectionState _state;
    private string? _lastError;
    private DateTime? _lastSuccessUtc;
    private DateTime? _lastFailureUtc;
    private DateTime? _demotedUntilUtc;
    private int _consecutiveFailures;
    private bool _recoveryPending;
    private bool _disposed;

    public DeviceSession(
        DeviceConfig.DeviceEntry configuration,
        ChannelEntry channel,
        IReadOnlyList<DevicePoint> points,
        SimulationConfig simulation,
        IClock clock,
        ChannelManager channels,
        string revision,
        IS7PlcClientFactory? s7ClientFactory = null,
        IDeviceEventSink? events = null,
        IModbusClientFactory? modbusClientFactory = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _points = points ?? throw new ArgumentNullException(nameof(points));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _revision = revision ?? string.Empty;
        _s7ClientFactory = s7ClientFactory;
        _events = events;
        _modbusClientFactory = modbusClientFactory;
        _state = DeviceConnectionState.Unknown;
    }

    public string DeviceId => _configuration.Id;
    public string ChannelId => _configuration.ChannelId;
    public DeviceMode Mode => _configuration.DeviceMode;
    public IReadOnlyList<DevicePoint> Points => _points;
    public DeviceConnectionState ConnectionState => _state;
    public bool IsOperational => _state is DeviceConnectionState.Online or DeviceConnectionState.Degraded
        && !IsDemoted;
    public RuntimeActivationReport ActivationReport
        => _runtime?.ActivationReport ?? new RuntimeActivationReport(
            1,
            IsOperational ? 1 : 0,
            IsOperational ? Array.Empty<DeviceActivationIssue>() : new[]
            {
                new DeviceActivationIssue(DeviceId, _configuration.Name, _lastError ?? "设备尚未激活")
            });

    public DeviceRuntimeInfo Status
    {
        get
        {
            var runtimeStatus = _runtime?.Status;
            var health = IsDemoted
                ? DeviceHealth.Degraded
                : _state switch
                {
                    DeviceConnectionState.Online => DeviceHealth.Healthy,
                    DeviceConnectionState.Degraded => DeviceHealth.Degraded,
                    // Modbus 传输层打开后、首次有效点位响应前，明确显示为“待点位验证”。
                    DeviceConnectionState.Connecting => DeviceHealth.Degraded,
                    DeviceConnectionState.Offline => DeviceHealth.Disconnected,
                    DeviceConnectionState.Faulted => DeviceHealth.Faulted,
                    _ => DeviceHealth.Unknown
                };
            var protocolText = DevicePointTypeCatalog.TryParseDriverKey(_configuration.DriverKey, out var protocol)
                ? DevicePointTypeCatalog.ToDisplayName(protocol)
                : "未知通信方式";
            var s7Endpoint = _configuration.SiemensS7;
            var address = s7Endpoint is not null && !string.IsNullOrWhiteSpace(s7Endpoint.Host)
                ? $"{s7Endpoint.Host}:{s7Endpoint.Port} rack={s7Endpoint.Rack} slot={s7Endpoint.Slot}"
                : _configuration.ModbusTcp is { } modbusTcp
                    ? $"{modbusTcp.Host}:{modbusTcp.Port}，站号 {_configuration.ModbusUnitId?.ToString() ?? "未配置"}"
                    : string.Equals(_configuration.DriverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase)
                        ? $"{_channel.Serial?.PortName ?? "串口"}，站号 {_configuration.ModbusUnitId?.ToString() ?? "未配置"}"
                        : _channel.TransportKind switch
                        {
                            ChannelTransportKind.Tcp => $"{_channel.Tcp?.Host}:{_channel.Tcp?.Port}",
                            ChannelTransportKind.Serial => _channel.Serial?.PortName ?? "串口",
                            ChannelTransportKind.Simulation => "仿真",
                            _ => "未知通道"
                        };
            return new DeviceRuntimeInfo(
                _configuration.Name,
                protocolText,
                address,
                IsSimulation: Mode == DeviceMode.Simulation,
                health,
                IsConnected: IsOperational,
                _lastError ?? runtimeStatus?.LastError,
                DeviceId,
                ChannelId,
                _revision,
                runtimeStatus?.ConnectionGeneration ?? 0,
                _state,
                Mode,
                _lastSuccessUtc ?? runtimeStatus?.LastSuccessUtc,
                _lastFailureUtc ?? runtimeStatus?.LastFailureUtc,
                _consecutiveFailures,
                _demotedUntilUtc,
                runtimeStatus?.NegotiatedPduSize);
        }
    }

    public async Task<SessionStartResult> StartAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsOperational)
                return new SessionStartResult(true, null, ActivationReport);

            await _channels.StartAsync(ct);
            _state = DeviceConnectionState.Connecting;
            _lastError = null;
            try
            {
                _runtime ??= CreateRuntime();
                await _runtime.StartAsync(ct);
                // S7/仿真运行时启动即完成可用连接；Modbus 只完成传输层打开，
                // 必须保留 Connecting，直到首次点位响应成功。
                _state = _runtime.Status.ConnectionState switch
                {
                    DeviceConnectionState.Connecting => DeviceConnectionState.Connecting,
                    DeviceConnectionState.Degraded => DeviceConnectionState.Degraded,
                    DeviceConnectionState.Online => DeviceConnectionState.Online,
                    _ when Mode == DeviceMode.Simulation => DeviceConnectionState.Online,
                    _ => DeviceConnectionState.Connecting
                };
                _consecutiveFailures = 0;
                _demotedUntilUtc = null;
                StartPollingIfRequired();
                return new SessionStartResult(true, null, ActivationReport);
            }
            catch (OperationCanceledException)
            {
                _state = DeviceConnectionState.Unknown;
                throw;
            }
            catch (Exception ex)
            {
                _state = DeviceConnectionState.Faulted;
                _lastError = ex.Message;
                _lastFailureUtc = _clock.UtcNow;
                if (_runtime is not null)
                {
                    await _runtime.DisposeAsync();
                    _runtime = null;
                }
                return new SessionStartResult(false, ex.Message, ActivationReport);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            StopPolling();
            if (_pollTask is not null)
            {
                try { await _pollTask; } catch (OperationCanceledException) { }
                _pollTask = null;
            }
            if (_runtime is not null)
                await _runtime.StopAsync(ct);
            _state = DeviceConnectionState.Offline;
            _demotedUntilUtc = null;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_points);
    }

    /// <summary>
    /// 普通读取优先返回缓存；没有缓存时才执行一次新鲜读取。
    /// </summary>
    public async Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
    {
        var current = ResolvePoint(point);
        if (TryGetCachedValue(current.PointId, out var cached)) return cached;
        return await ReadFreshAsync(current.PointId, ct);
    }

    public async Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
    {
        var point = ResolvePoint(pointId);
        EnsureReadable();
        var values = await ExecuteWithRetryAsync(
            runtime => runtime.ReadManyFreshAsync(new[] { point.PointId }, ct), ct);
        var value = values.Single();
        Cache(value);
        return value;
    }

    public async Task<IReadOnlyList<PointValue>> ReadManyFreshAsync(
        IReadOnlyCollection<string> pointIds,
        CancellationToken ct = default)
    {
        var points = pointIds.Select(ResolvePoint).ToList();
        if (points.Count == 0) return Array.Empty<PointValue>();
        EnsureReadable();
        var values = await ExecuteWithRetryAsync(
            runtime => runtime.ReadManyFreshAsync(points.Select(point => point.PointId).ToArray(), ct), ct);
        foreach (var value in values) Cache(value);
        return values;
    }

    public async Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
    {
        var current = ResolvePoint(point);
        EnsureOperational(allowDemotedWrite: true);
        // 降级期禁止写入是固定安全规则；旧配置中的 DiscardWritesWhileDemoted 只保留用于兼容读取。
        if (IsDemoted)
            throw new DomainException($"设备 {_configuration.Code} 当前处于降级期，写入已丢弃");

        try
        {
            // 写入是高风险动作：取消、超时或异常后的结果可能已经落到设备，
            // 因此这里禁止按读取策略自动重发。
            var result = await _channels.ExecuteAsync(
                ChannelId,
                DeviceId,
                _ => _runtime!.WriteAsync(current, value, ct),
                ct);
            Cache(result);
            RegisterSuccess();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RegisterFailure(ex);
            throw;
        }
    }

    public bool TryGetCachedValue(string pointId, out PointValue value)
    {
        if (_cache.TryGetValue(pointId.Trim(), out var cached))
        {
            value = ApplyStale(cached);
            return true;
        }
        value = default!;
        return false;
    }

    public IReadOnlyList<PointValue> ListCachedValues()
        => _cache.Values.Select(ApplyStale).OrderBy(value => value.PointCode, StringComparer.OrdinalIgnoreCase).ToList();

    public DevicePoint? GetPoint(string pointId)
        => _points.FirstOrDefault(point => string.Equals(point.PointId, pointId, StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await StopAsync(); }
        finally
        {
            if (_runtime is not null)
                await _runtime.DisposeAsync();
            _pollCts?.Dispose();
            _lifecycle.Dispose();
        }
    }

    private IDeviceRuntime CreateRuntime()
    {
        if (Mode == DeviceMode.Simulation)
            return new SimulationDeviceRuntime(_configuration, _points, _simulation, _clock, _revision);
        if (string.Equals(_configuration.DriverKey, DriverKeyCatalog.SiemensS7, StringComparison.OrdinalIgnoreCase))
            return new S7NetPlusDeviceRuntime(_configuration, _channel, _points, _clock, _revision, _s7ClientFactory, _events);
        if (string.Equals(_configuration.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase))
            return new ModbusTcpDeviceRuntime(
                _configuration,
                _channel,
                _points,
                _clock,
                _revision,
                _modbusClientFactory,
                _events);
        if (string.Equals(_configuration.DriverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase))
            return new ModbusRtuDeviceRuntime(
                _configuration,
                _channel,
                _points,
                _clock,
                _revision,
                _channels.GetOrCreateModbusRtuConnection(ChannelId),
                _events);
        throw new DomainException($"设备 {_configuration.Code} 当前不能在硬件模式使用");
    }

    private async Task<IReadOnlyList<PointValue>> ExecuteWithRetryAsync(
        Func<IDeviceRuntime, Task<IReadOnlyList<PointValue>>> operation,
        CancellationToken ct)
    {
        var timing = _configuration.Timing ?? new DeviceTimingOptions();
        var attempts = Math.Max(0, timing.RetryCount) + 1;
        Exception? last = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var result = await _channels.ExecuteAsync(
                    ChannelId,
                    DeviceId,
                    _ => operation(_runtime!),
                    ct);
                RegisterSuccess();
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                RegisterFailure(ex);
                if (attempt == attempts) break;
                if (timing.InterRequestDelayMs > 0)
                    await Task.Delay(timing.InterRequestDelayMs, ct);
            }
        }
        throw last ?? new DomainException($"设备 {_configuration.Code} 读取失败");
    }

    private void StartPollingIfRequired()
    {
        if (_configuration.ScanMode != DeviceScanMode.FixedInterval || _points.Count == 0 || _pollTask is not null)
            return;
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        _pollTask = PollAsync(_pollCts.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(10, _configuration.PollIntervalMs));
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!CanPoll) continue;
                try
                {
                    await ReadManyFreshAsync(_points.Select(point => point.PointId).ToArray(), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex) { RegisterFailure(ex); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private DevicePoint ResolvePoint(string pointId)
        => string.IsNullOrWhiteSpace(pointId) || GetPoint(pointId.Trim()) is not { } point
            ? throw new DomainException($"点位 {pointId} 不属于设备 {_configuration.Code}")
            : point;

    private DevicePoint ResolvePoint(DevicePoint requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (!string.IsNullOrWhiteSpace(requested.PointId) && GetPoint(requested.PointId) is { } byId)
            return byId;
        if (string.IsNullOrWhiteSpace(requested.PointId)
            && _points.FirstOrDefault(point => string.Equals(point.Address, requested.Address, StringComparison.OrdinalIgnoreCase)) is { } byAddress)
            return byAddress;
        throw new DomainException($"点位 {requested.PointId}/{requested.Address} 不属于设备 {_configuration.Code}");
    }

    private void EnsureOperational(bool allowDemotedWrite = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TryRecoverDemotion();
        if (_state is not (DeviceConnectionState.Online or DeviceConnectionState.Degraded))
        {
            var detail = string.IsNullOrWhiteSpace(_lastError) ? "请先建立连接" : _lastError;
            throw new DomainException($"设备 {_configuration.Code} 当前不可用：{detail}");
        }
        if (IsDemoted && !allowDemotedWrite)
            throw new DomainException($"设备 {_configuration.Code} 当前处于降级期，暂不执行新鲜读取");
    }

    /// <summary>
    /// 读取可以用于 Connecting/Offline 状态下的恢复探测；写入仍只能走 EnsureOperational。
    /// </summary>
    private void EnsureReadable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TryRecoverDemotion();
        if (_state is not (DeviceConnectionState.Connecting or DeviceConnectionState.Online
            or DeviceConnectionState.Degraded or DeviceConnectionState.Offline))
        {
            var detail = string.IsNullOrWhiteSpace(_lastError) ? "请先建立连接" : _lastError;
            throw new DomainException($"设备 {_configuration.Code} 当前不可读取：{detail}");
        }
        if (IsDemoted)
            throw new DomainException($"设备 {_configuration.Code} 当前处于降级期，暂不执行新鲜读取");
    }

    private bool CanPoll
        => _runtime is not null
            && _state is (DeviceConnectionState.Connecting or DeviceConnectionState.Online
                or DeviceConnectionState.Degraded or DeviceConnectionState.Offline)
            && !IsDemoted;

    private bool IsDemoted => _demotedUntilUtc.HasValue && _demotedUntilUtc.Value > _clock.UtcNow;

    private void TryRecoverDemotion()
    {
        if (_demotedUntilUtc.HasValue && _demotedUntilUtc.Value <= _clock.UtcNow)
        {
            _demotedUntilUtc = null;
            _consecutiveFailures = 0;
            if (_state == DeviceConnectionState.Degraded)
                _state = DeviceConnectionState.Online;
            _recoveryPending = true;
            Publish(DeviceEventSeverity.Information, "S7_RECOVERY_ATTEMPT",
                $"设备 {_configuration.Code} 降级期结束，开始执行一次恢复探测");
        }
    }

    private void RegisterSuccess()
    {
        _lastSuccessUtc = _clock.UtcNow;
        _lastError = null;
        _consecutiveFailures = 0;
        _demotedUntilUtc = null;
        if (_state is DeviceConnectionState.Connecting or DeviceConnectionState.Offline
            or DeviceConnectionState.Degraded)
            _state = DeviceConnectionState.Online;
        if (_recoveryPending)
        {
            _recoveryPending = false;
            Publish(DeviceEventSeverity.Information, "S7_RECOVERED",
                $"设备 {_configuration.Code} 恢复通信，已清零连续失败计数");
        }
    }

    private void RegisterFailure(Exception ex)
    {
        _lastFailureUtc = _clock.UtcNow;
        _lastError = ex.Message;
        _consecutiveFailures++;
        if (_state == DeviceConnectionState.Connecting)
            _state = DeviceConnectionState.Offline;
        else if (_state == DeviceConnectionState.Online)
            _state = DeviceConnectionState.Degraded;
        var demotion = _configuration.AutoDemotion ?? new DeviceDemotionOptions();
        if (demotion.Enabled && _consecutiveFailures >= demotion.FailureThreshold)
        {
            _recoveryPending = false;
            _demotedUntilUtc = _clock.UtcNow.AddMilliseconds(demotion.DemotionPeriodMs);
            Publish(DeviceEventSeverity.Warning, "S7_DEVICE_DEMOTED", $"设备 {_configuration.Code} 连续失败 {_consecutiveFailures} 次，进入降级期");
        }
    }

    private void Cache(PointValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.PointId))
            _cache[value.PointId] = value;
        _lastSuccessUtc = value.TimestampUtc;
    }

    private PointValue ApplyStale(PointValue value)
    {
        if (value.Quality == PointQuality.Good
            && _configuration.StaleAfterMs > 0
            && _clock.UtcNow - value.TimestampUtc > TimeSpan.FromMilliseconds(_configuration.StaleAfterMs))
            return value with { Quality = PointQuality.Stale };
        return value;
    }

    private void Publish(DeviceEventSeverity severity, string code, string message)
    {
        _events?.Publish(new DeviceCommunicationEvent(
            _clock.UtcNow,
            severity,
            ChannelId,
            DeviceId,
            "DeviceSession",
            code,
            message));
    }
}

public sealed record SessionStartResult(
    bool Ok,
    string? Error,
    RuntimeActivationReport? ActivationReport = null);
