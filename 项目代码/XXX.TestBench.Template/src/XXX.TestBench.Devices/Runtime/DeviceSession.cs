using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Simulation;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 单设备会话：持有该设备的缓存、状态和采集运行时，但通过 ChannelManager 使用共享通道。
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
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private IDeviceRuntime? _runtime;
    private DeviceConnectionState _state;
    private string? _lastError;
    private bool _disposed;

    public DeviceSession(
        DeviceConfig.DeviceEntry configuration,
        ChannelEntry channel,
        IReadOnlyList<DevicePoint> points,
        SimulationConfig simulation,
        IClock clock,
        ChannelManager channels,
        string revision)
    {
        _configuration = configuration;
        _channel = channel;
        _points = points;
        _simulation = simulation;
        _clock = clock;
        _channels = channels;
        _revision = revision;
        _state = configuration.Enabled ? DeviceConnectionState.Unknown : DeviceConnectionState.Disabled;
    }

    public string DeviceId => _configuration.Id;
    public string ChannelId => _configuration.ChannelId;
    public IReadOnlyList<DevicePoint> Points => _points;
    public DeviceConnectionState ConnectionState => _state;
    public bool IsOperational => _state is DeviceConnectionState.Online or DeviceConnectionState.Degraded;

    public DeviceRuntimeInfo Status
    {
        get
        {
            var health = _state switch
            {
                DeviceConnectionState.Online => DeviceHealth.Healthy,
                DeviceConnectionState.Degraded => DeviceHealth.Degraded,
                DeviceConnectionState.Offline => DeviceHealth.Disconnected,
                DeviceConnectionState.Faulted => DeviceHealth.Faulted,
                _ => DeviceHealth.Unknown
            };
            return new DeviceRuntimeInfo(
                _configuration.Name,
                _configuration.DriverKey,
                _channel.TransportKind.ToString(),
                IsSimulation: string.Equals(_configuration.DriverKey, DriverKeyCatalog.Simulation, StringComparison.OrdinalIgnoreCase),
                health,
                IsConnected: IsOperational,
                _lastError,
                DeviceId,
                ChannelId,
                _revision,
                _runtime?.Status.ConnectionGeneration ?? 0,
                _state);
        }
    }

    public async Task<SessionStartResult> StartAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_configuration.Enabled)
            {
                _state = DeviceConnectionState.Disabled;
                return new SessionStartResult(true, null);
            }
            if (IsOperational) return new SessionStartResult(true, null);

            _state = DeviceConnectionState.Connecting;
            _lastError = null;
            try
            {
                if (!string.Equals(_configuration.DriverKey, DriverKeyCatalog.Simulation, StringComparison.OrdinalIgnoreCase))
                    throw new DomainException($"设备 {_configuration.Code} 的驱动尚未实现：{_configuration.DriverKey}");
                if (_channel.TransportKind != ChannelTransportKind.Simulation)
                    throw new DomainException($"仿真设备 {_configuration.Code} 必须使用仿真通道");

                _runtime = new SimulationDeviceRuntime(_configuration, _points, _simulation, _clock, _revision);
                await _runtime.StartAsync(ct);
                _state = DeviceConnectionState.Online;
                return new SessionStartResult(true, null);
            }
            catch (Exception ex)
            {
                _state = DeviceConnectionState.Faulted;
                _lastError = ex.Message;
                if (_runtime is not null)
                {
                    await _runtime.DisposeAsync();
                    _runtime = null;
                }
                return new SessionStartResult(false, ex.Message);
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
            if (_runtime is not null)
                await _runtime.StopAsync(ct);
            _state = _configuration.Enabled ? DeviceConnectionState.Offline : DeviceConnectionState.Disabled;
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

    public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
        => ExecuteOnChannelAsync(point, runtime => runtime.ReadAsync(point, ct), ct);

    public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
        => ExecuteOnChannelAsync(pointId, runtime => runtime.ReadFreshAsync(pointId, ct), ct);

    public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
        => ExecuteOnChannelAsync(point, runtime => runtime.WriteAsync(point, value, ct), ct);

    public DevicePoint? GetPoint(string pointId)
        => _points.FirstOrDefault(point => string.Equals(point.PointId, pointId, StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await StopAsync(); }
        finally
        {
            if (_runtime is not null) await _runtime.DisposeAsync();
            _lifecycle.Dispose();
        }
    }

    private async Task<PointValue> ExecuteOnChannelAsync(
        DevicePoint point,
        Func<IDeviceRuntime, Task<PointValue>> operation,
        CancellationToken ct)
    {
        EnsureOperational();
        return await _channels.ExecuteAsync(
            ChannelId,
            _ => operation(_runtime!),
            ct);
    }

    private async Task<PointValue> ExecuteOnChannelAsync(
        string pointId,
        Func<IDeviceRuntime, Task<PointValue>> operation,
        CancellationToken ct)
    {
        EnsureOperational();
        if (GetPoint(pointId) is null)
            throw new DomainException($"点位 {pointId} 不属于设备 {_configuration.Code}");
        return await _channels.ExecuteAsync(
            ChannelId,
            _ => operation(_runtime!),
            ct);
    }

    private void EnsureOperational()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsOperational || _runtime is null)
            throw new DomainException($"设备 {_configuration.Code} 当前不可用：{_state}，{_lastError}");
    }
}

public sealed record SessionStartResult(bool Ok, string? Error);
