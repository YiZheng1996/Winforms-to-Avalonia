using System.Collections.Concurrent;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Devices.Simulation;

/// <summary>
/// 仿真设备运行时：只操作仿真状态，不连接真实设备；界面与记录必须显示仿真标识。
/// 读取按 simulation.json 变化规则演进，故障注入可使点位质量变 Stale。
/// </summary>
public sealed class SimulationDeviceRuntime : IDeviceRuntime
{
    private readonly IReadOnlyDictionary<string, DevicePoint> _pointsByAddress;
    private readonly SimulationConfig _config;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, object?> _values = new();
    private readonly object _gate = new();
    private DateTime _startedUtc;
    private bool _started;

    public SimulationDeviceRuntime(string name, IReadOnlyList<DevicePoint> points, SimulationConfig config, IClock clock)
    {
        Name = name;
        _config = config;
        _clock = clock;
        _pointsByAddress = points.Where(p => p.Protocol.Equals("Simulation", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Address, StringComparer.Ordinal);
        foreach (var kv in config.InitialValues)
            _values[kv.Key] = kv.Value;
    }

    public string Name { get; }
    public DeviceMode Mode => DeviceMode.Simulation;
    public bool IsSimulation => true;

    public DeviceRuntimeInfo Status => new(
        Name,
        "Simulation",
        "sim://" + Name,
        IsSimulation: true,
        Health: _started ? DeviceHealth.Healthy : DeviceHealth.Unknown,
        IsConnected: _started,
        LastError: null);

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _started = true;
            _startedUtc = _clock.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_gate) _started = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DevicePoint>>(_pointsByAddress.Values.ToList());

    public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
    {
        if (!_pointsByAddress.TryGetValue(point.Address, out _))
            throw new Core.Common.DomainException($"点位地址 {point.Address} 不在仿真运行时");

        var quality = GetQuality(point.Address);
        var value = ComputeValue(point.Address);
        return Task.FromResult(new PointValue(point.Code, point.Address, quality, value, _clock.UtcNow));
    }

    public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
    {
        if (!point.IsWritable)
            throw new Core.Common.DomainException($"点位 {point.Code} 不可写");
        if (!_pointsByAddress.ContainsKey(point.Address))
            throw new Core.Common.DomainException($"点位地址 {point.Address} 不在仿真运行时");
        _values[point.Address] = value;
        return Task.FromResult(new PointValue(point.Code, point.Address, PointQuality.Good, value, _clock.UtcNow));
    }

    private PointQuality GetQuality(string address)
    {
        lock (_gate)
        {
            if (!_started) return PointQuality.Unknown;
            var injected = _config.FaultInjectionScenarios.FirstOrDefault(f =>
                f.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
            return injected is not null && injected.Behavior.Equals("stale", StringComparison.OrdinalIgnoreCase)
                ? PointQuality.Stale
                : PointQuality.Good;
        }
    }

    private object? ComputeValue(string address)
    {
        if (_values.TryGetValue(address, out var initial))
        {
            lock (_gate)
            {
                var rule = _config.ChangeRules.FirstOrDefault(r => r.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
                if (rule is null) return initial;
                if (double.TryParse(initial?.ToString(), out var start))
                {
                    var elapsed = (_clock.UtcNow - _startedUtc).TotalSeconds;
                    var value = rule.Pattern switch
                    {
                        "ramp" => start + rule.RatePerSecond * elapsed,
                        "sine" => start + rule.RatePerSecond * Math.Sin(elapsed),
                        _ => start
                    };
                    var clamped = Math.Clamp(value, rule.Min, rule.Max);
                    return Math.Round(clamped, 4);
                }
            }
        }
        return initial;
    }
}
