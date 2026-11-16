using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;
using System.Text.Json;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 单次仿真运行的只读许可。固定运行时、配置和连接代次；撤销后即使重连也不能复用。
/// 此实现从不调用 Runtime.WriteAsync，不能用于真实或混合设备。
/// </summary>
internal sealed class SimulationRunDeviceAccess : IBenchDeviceAccess
{
    private readonly IDeviceRuntime _runtime;
    private readonly Func<IDeviceRuntime?> _current;
    private readonly IReadOnlyDictionary<string, ResolvedSignal> _signals;
    private readonly Dictionary<string, long> _generations;
    private readonly string _revision;
    private readonly IClock _clock;
    private int _revoked;

    internal SimulationRunDeviceAccess(IDeviceRuntime runtime, Func<IDeviceRuntime?> current,
        IReadOnlyDictionary<string, ResolvedSignal> signals, string revision, string preparedVersionVector, IClock clock)
    {
        _runtime = runtime;
        _current = current;
        _signals = signals;
        _revision = revision;
        _clock = clock;
        if (signals.Values.Any(signal => signal.Requirement.Access != SignalAccessKind.Read))
            throw new DomainException("当前仿真闭环只允许声明只读信号的执行器。");
        // 使用准备时的代次，而不是开始事务前重新接纳一个已经重连的新代次。
        using var vector = JsonDocument.Parse(preparedVersionVector);
        _generations = vector.RootElement.GetProperty("DeviceSessions").EnumerateArray().ToDictionary(
            device => device.GetProperty("DeviceId").GetString()!,
            device => device.GetProperty("ConnectionGeneration").GetInt64(), StringComparer.OrdinalIgnoreCase);
        if (signals.Values.Any(signal => !_generations.ContainsKey(signal.Point.DeviceId)))
            throw new DomainException("准备快照缺少必需设备的连接代次。");
        EnsureValid();
    }

    internal void Revoke() => Interlocked.Exchange(ref _revoked, 1);

    internal void EnsureValid()
    {
        if (Volatile.Read(ref _revoked) != 0)
            throw new DomainException("本次仿真操作许可已撤销。");
        if (!ReferenceEquals(_runtime, _current()) || !_runtime.IsSimulation
            || _runtime.Mode != DeviceMode.Simulation || _runtime.ActiveRevision != _revision)
        {
            Revoke();
            throw new DomainException("仿真运行时或配置已变化，禁止继续执行。");
        }
        foreach (var (id, generation) in _generations)
        {
            var status = _runtime.GetDeviceStatus(id);
            if (!status.IsSimulation || status.Mode != DeviceMode.Simulation || !status.IsConnected
                || status.Health != DeviceHealth.Healthy || generation <= 0
                || status.ConnectionGeneration != generation || status.Revision != _revision)
            {
                Revoke();
                throw new DomainException("仿真设备断开、重连或配置变化，原运行许可已失效。");
            }
        }
    }

    public async Task<PointValue> ReadFreshAsync(string signalKey, CancellationToken cancellationToken = default)
    {
        EnsureValid();
        if (!_signals.TryGetValue(signalKey, out var signal))
            throw new DomainException("执行器读取了未声明的信号。");
        var value = await _runtime.ReadFreshAsync(signal.PointId, cancellationToken);
        EnsureValid();
        if (value.PointId != signal.PointId || value.DeviceId != signal.Point.DeviceId
            || value.Revision != _revision || value.ConnectionGeneration != _generations[value.DeviceId]
            || value.Quality != PointQuality.Good || value.TimestampUtc.Kind != DateTimeKind.Utc
            || value.TimestampUtc > _clock.UtcNow
            || _clock.UtcNow - value.TimestampUtc > signal.Requirement.MaxSampleAge)
            throw new DomainException("测量样本身份、连接代次、质量或时间无效。");
        return value;
    }

    public Task<BenchDeviceWriteResult> WriteAsync(string signalKey, object value, CancellationToken cancellationToken = default)
        => throw new DomainException("当前仿真运行许可不包含设备写入能力。");
}
