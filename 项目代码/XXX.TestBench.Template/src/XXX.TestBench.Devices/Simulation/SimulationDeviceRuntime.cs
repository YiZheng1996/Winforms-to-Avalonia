using System.Collections.Concurrent;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Devices.Simulation;

/// <summary>
/// 仿真设备运行时：只操作仿真状态，不连接真实设备。
/// v2 配置按 PointId 隔离状态；无身份的 v1 点位继续按地址运行，仅作为迁移兼容。
/// </summary>
public sealed class SimulationDeviceRuntime : IDeviceRuntime
{
    private readonly IReadOnlyDictionary<string, DevicePoint> _pointsById;
    private readonly IReadOnlyDictionary<string, DevicePoint> _pointsByAddress;
    private readonly SimulationConfig _config;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly string _deviceId;
    private readonly string _channelId;
    private readonly string _revision;
    private DateTime _startedUtc;
    private bool _started;
    private long _connectionGeneration;

    /// <summary>
    /// v1 兼容构造函数。
    /// </summary>
    public SimulationDeviceRuntime(string name, IReadOnlyList<DevicePoint> points, SimulationConfig config, IClock clock)
        : this(name, string.Empty, string.Empty, string.Empty, points, config, clock)
    {
    }

    /// <summary>
    /// v2 单设备构造函数。
    /// </summary>
    public SimulationDeviceRuntime(
        DeviceConfig.DeviceEntry device,
        IReadOnlyList<DevicePoint> points,
        SimulationConfig config,
        IClock clock,
        string revision = "")
        : this(device.Name, device.Id, device.ChannelId, revision, points, config, clock)
    {
    }

    private SimulationDeviceRuntime(
        string name,
        string deviceId,
        string channelId,
        string revision,
        IReadOnlyList<DevicePoint> points,
        SimulationConfig config,
        IClock clock)
    {
        Name = name;
        _deviceId = deviceId;
        _channelId = channelId;
        _revision = revision;
        _config = config;
        _clock = clock;

        var enabledPoints = points
            .Where(point => point.IsEnabled)
            .Select(point => !string.IsNullOrWhiteSpace(revision) && string.IsNullOrWhiteSpace(point.Revision)
                ? point with { Revision = revision }
                : point)
            .ToList();
        _pointsById = enabledPoints
            .Where(point => !string.IsNullOrWhiteSpace(point.PointId))
            .ToDictionary(point => point.PointId, StringComparer.OrdinalIgnoreCase);
        _pointsByAddress = enabledPoints
            .ToDictionary(point => point.Address, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in config.InitialValues)
            _values[kv.Key] = Normalize(kv.Value);
        foreach (var kv in config.InitialValuesByPointId)
            _values[kv.Key] = Normalize(kv.Value);
    }

    public string Name { get; }
    public DeviceMode Mode => DeviceMode.Simulation;
    public bool IsSimulation => true;
    public string ActiveRevision => _revision;

    public DeviceRuntimeInfo Status
    {
        get
        {
            lock (_gate)
            {
                return new DeviceRuntimeInfo(
                    Name,
                    "Simulation",
                    "sim://" + Name,
                    IsSimulation: true,
                    Health: _started ? DeviceHealth.Healthy : DeviceHealth.Unknown,
                    IsConnected: _started,
                    LastError: null,
                    DeviceId: _deviceId,
                    ChannelId: _channelId,
                    Revision: _revision,
                    ConnectionGeneration: _connectionGeneration);
            }
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_started) return Task.CompletedTask;
            _started = true;
            _startedUtc = _clock.UtcNow;
            _connectionGeneration++;
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate) _started = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DevicePoint>>(_pointsByAddress.Values.ToList());
    }

    public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_pointsById.TryGetValue(pointId, out var point))
            throw new DomainException($"点位 {pointId} 不在仿真设备 {Name}");
        return ReadAsync(point, ct);
    }

    public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var current = ResolvePoint(point);
        var quality = GetQuality(current);
        var value = quality == PointQuality.Unknown ? null : ComputeValue(current);
        return Task.FromResult(CreateValue(current, quality, value));
    }

    public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var current = ResolvePoint(point);
        if (!current.IsWritable)
            throw new DomainException($"点位 {current.Code} 不可写");
        lock (_gate)
        {
            if (!_started) throw new DomainException($"仿真设备 {Name} 尚未启动");
        }

        _values[KeyFor(current)] = value;
        return Task.FromResult(CreateValue(current, PointQuality.Good, value));
    }

    public DevicePoint? GetPoint(string pointId)
        => _pointsById.TryGetValue(pointId, out var point) ? point : null;

    private DevicePoint ResolvePoint(DevicePoint requested)
    {
        DevicePoint? current = null;
        if (!string.IsNullOrWhiteSpace(requested.PointId))
            _pointsById.TryGetValue(requested.PointId, out current);
        if (current is null && string.IsNullOrWhiteSpace(requested.PointId))
            _pointsByAddress.TryGetValue(requested.Address, out current);
        if (current is null)
            throw new DomainException($"点位 {requested.PointId}/{requested.Address} 不在仿真设备 {Name}");
        if (!string.IsNullOrWhiteSpace(requested.DeviceId)
            && !string.IsNullOrWhiteSpace(_deviceId)
            && !string.Equals(requested.DeviceId, _deviceId, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"点位 {requested.Code} 不属于仿真设备 {Name}");
        if (!string.IsNullOrWhiteSpace(requested.Code)
            && !string.Equals(requested.Code, current.Code, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"点位身份已变化：{requested.Code}");
        return current;
    }

    private PointValue CreateValue(DevicePoint point, PointQuality quality, object? value)
        => new(point.Code, point.Address, quality, value, _clock.UtcNow,
            point.PointId, _deviceId, _connectionGeneration, _revision);

    private string KeyFor(DevicePoint point)
        => string.IsNullOrWhiteSpace(point.PointId) ? point.Address : point.PointId;

    private PointQuality GetQuality(DevicePoint point)
    {
        lock (_gate)
        {
            if (!_started) return PointQuality.Unknown;
            var injected = _config.SchemaVersion == SimulationConfig.LegacySchemaVersion
                ? _config.FaultInjectionScenarios.FirstOrDefault(f =>
                    f.Address.Equals(point.Address, StringComparison.OrdinalIgnoreCase))
                : _config.FaultInjectionScenarios.FirstOrDefault(f =>
                    string.Equals(f.PointId, point.PointId, StringComparison.OrdinalIgnoreCase));
            return injected is not null && injected.BehaviorKind == SimulationFaultBehavior.Stale
                ? PointQuality.Stale
                : PointQuality.Good;
        }
    }

    private object? ComputeValue(DevicePoint point)
    {
        var key = KeyFor(point);
        _values.TryGetValue(key, out var initial);
        lock (_gate)
        {
            var rule = _config.SchemaVersion == SimulationConfig.LegacySchemaVersion
                ? _config.ChangeRules.FirstOrDefault(item =>
                    item.Address.Equals(point.Address, StringComparison.OrdinalIgnoreCase))
                : _config.ChangeRules.FirstOrDefault(item =>
                    string.Equals(item.PointId, point.PointId, StringComparison.OrdinalIgnoreCase));
            if (rule is null) return initial;
            if (double.TryParse(initial?.ToString(), out var start))
            {
                var elapsed = (_clock.UtcNow - _startedUtc).TotalSeconds;
                var value = rule.PatternKind switch
                {
                    SimulationChangePattern.Ramp => start + rule.RatePerSecond * elapsed,
                    SimulationChangePattern.Sine => start + rule.RatePerSecond * Math.Sin(elapsed),
                    _ => start
                };
                return Math.Round(Math.Clamp(value, rule.Min, rule.Max), 4);
            }
            return initial;
        }
    }

    private static object? Normalize(object? value)
    {
        if (value is not System.Text.Json.JsonElement element) return value;
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => element.TryGetDouble(out var d) ? d : element.GetRawText(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.String => element.GetString(),
            _ => element.GetRawText()
        };
    }
}
