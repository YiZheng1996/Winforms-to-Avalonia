using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Devices.Simulation;

/// <summary>
/// 仿真设备运行时按 PointId 隔离状态，不用地址推断目标点位。
/// </summary>
public sealed class SimulationDeviceRuntime : IDeviceRuntime
{
    private readonly IReadOnlyDictionary<string, DevicePoint> _pointsById;
    private readonly SimulationConfig _config;
    private readonly IClock _clock;
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PointValue> _cachedValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly string _deviceId;
    private readonly string _channelId;
    private readonly string _revision;
    private readonly SignalBindingsConfig _signalBindings = new();
    private DateTime _startedUtc;
    private bool _started;
    private long _connectionGeneration;
    private RuntimeActivationReport _activationReport;

    public SimulationDeviceRuntime(
        DeviceConfig.DeviceEntry device,
        IReadOnlyList<DevicePoint> points,
        SimulationConfig config,
        IClock clock,
        string revision)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Guid.TryParse(device.Id, out _) || string.IsNullOrWhiteSpace(device.ChannelId))
            throw new DomainException("仿真运行时必须提供有效设备身份和通道身份");
        if (string.IsNullOrWhiteSpace(revision))
            throw new DomainException("仿真运行时必须提供完整配置版本号");
        config.Validate();

        Name = device.Name;
        _deviceId = device.Id.Trim();
        _channelId = device.ChannelId.Trim();
        _revision = revision.Trim();
        _config = config;
        _clock = clock;
        _activationReport = new RuntimeActivationReport(1, 0,
            new[] { new DeviceActivationIssue(_deviceId, Name, "仿真设备尚未启动") });

        var pointMap = new Dictionary<string, DevicePoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            if (!Guid.TryParse(point.PointId, out _)
                || !string.Equals(point.DeviceId, _deviceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(point.Revision, _revision, StringComparison.Ordinal))
                throw new DomainException($"仿真点位 {point.Code} 缺少有效 PointId、DeviceId 或 Revision");
            if (!pointMap.TryAdd(point.PointId, point))
                throw new DomainException($"仿真配置存在重复 PointId：{point.PointId}");
        }
        _pointsById = pointMap;

        foreach (var pair in config.InitialValuesByPointId)
            if (_pointsById.ContainsKey(pair.Key))
                _values[pair.Key] = Normalize(pair.Value);
    }

    public string Name { get; }
    public DeviceMode Mode => DeviceMode.Simulation;
    public bool IsSimulation => true;
    public string ActiveRevision => _revision;
    public SignalBindingsConfig SignalBindings => _signalBindings;
    public RuntimeActivationReport ActivationReport => _activationReport;

    public DeviceRuntimeInfo Status
    {
        get
        {
            lock (_gate)
            {
                return new DeviceRuntimeInfo(
                    Name,
                    "Simulation",
                    "仿真",
                    IsSimulation: true,
                    Health: _started ? DeviceHealth.Healthy : DeviceHealth.Unknown,
                    IsConnected: _started,
                    LastError: _started ? null : "仿真设备尚未启动",
                    DeviceId: _deviceId,
                    ChannelId: _channelId,
                    Revision: _revision,
                    ConnectionGeneration: _connectionGeneration,
                    Mode: DeviceMode.Simulation);
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
            _activationReport = new RuntimeActivationReport(1, 1, Array.Empty<DeviceActivationIssue>());
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _started = false;
            _activationReport = new RuntimeActivationReport(1, 0,
                new[] { new DeviceActivationIssue(_deviceId, Name, "仿真设备已停止") });
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DevicePoint>>(_pointsById.Values.ToList());
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
        var rawValue = quality == PointQuality.Unknown ? null : ComputeValue(current);
        var value = CreateValue(current, quality, rawValue);
        lock (_gate) _cachedValues[current.PointId] = value;
        return Task.FromResult(value);
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
            _values[current.PointId] = value;
        }

        var sample = CreateValue(current, PointQuality.Good, value);
        lock (_gate) _cachedValues[current.PointId] = sample;
        return Task.FromResult(sample);
    }

    public bool TryGetCachedValue(string pointId, out PointValue value)
    {
        lock (_gate) return _cachedValues.TryGetValue(pointId, out value!);
    }

    public IReadOnlyList<PointValue> ListCachedValues()
    {
        lock (_gate) return _cachedValues.Values.ToList();
    }

    public DeviceRuntimeInfo GetDeviceStatus(string deviceId)
    {
        if (!string.Equals(deviceId, _deviceId, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"仿真设备 {Name} 不存在设备身份：{deviceId}");
        return Status;
    }

    public IReadOnlyList<DeviceRuntimeInfo> ListDeviceStatuses() => new[] { Status };

    public DevicePoint? GetPoint(string pointId)
        => _pointsById.TryGetValue(pointId, out var point) ? point : null;

    private DevicePoint ResolvePoint(DevicePoint requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (string.IsNullOrWhiteSpace(requested.PointId)
            || !_pointsById.TryGetValue(requested.PointId, out var current))
            throw new DomainException($"点位缺少有效 PointId，或不在仿真设备 {Name}");
        if (!string.Equals(requested.DeviceId, _deviceId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(requested.Revision, _revision, StringComparison.Ordinal))
            throw new DomainException($"点位 {requested.Code} 的设备身份或配置版本已变化");
        if (!string.Equals(requested.Code, current.Code, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"点位身份已变化：{requested.Code}");
        return current;
    }

    private PointValue CreateValue(DevicePoint point, PointQuality quality, object? rawValue)
        => new(point.Code, point.Address, quality,
            quality == PointQuality.Unknown ? null : DevicePointValueConverter.ToEngineering(point, rawValue),
            _clock.UtcNow,
            point.PointId, _deviceId, _connectionGeneration, _revision, rawValue);

    private PointQuality GetQuality(DevicePoint point)
    {
        lock (_gate)
        {
            if (!_started) return PointQuality.Unknown;
            var injected = _config.FaultInjectionScenarios.FirstOrDefault(f =>
                string.Equals(f.PointId, point.PointId, StringComparison.OrdinalIgnoreCase));
            return injected is not null && injected.BehaviorKind == SimulationFaultBehavior.Stale
                ? PointQuality.Stale
                : PointQuality.Good;
        }
    }

    private object? ComputeValue(DevicePoint point)
    {
        lock (_gate)
        {
            _values.TryGetValue(point.PointId, out var initial);
            var rule = _config.ChangeRules.FirstOrDefault(item =>
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
