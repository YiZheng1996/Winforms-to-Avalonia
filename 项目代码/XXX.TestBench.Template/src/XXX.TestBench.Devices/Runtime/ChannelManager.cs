using System.Collections.Concurrent;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 通道生命周期和资源调度器。串口通道全局串行；TCP/S7 通道按设备隔离连接并保证单设备串行。
/// </summary>
public sealed class ChannelManager : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<string, ChannelSession> _sessions;
    private readonly IModbusClientFactory _modbusClientFactory;
    private readonly ConcurrentDictionary<string, ModbusRtuChannelConnection> _rtuConnections = new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public ChannelManager(IEnumerable<ChannelEntry> channels, IModbusClientFactory? modbusClientFactory = null)
    {
        _modbusClientFactory = modbusClientFactory ?? new NModbusClientFactory();
        _sessions = channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Id))
            .GroupBy(channel => channel.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new ChannelSession(group.First()),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ChannelSession> Sessions => _sessions.Values.ToArray();

    /// <summary>
    /// 取得串口通道唯一的 RTU 连接对象。多个设备站号共享它，不会重复打开串口。
    /// </summary>
    public ModbusRtuChannelConnection GetOrCreateModbusRtuConnection(string channelId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_sessions.TryGetValue(channelId.Trim(), out var session))
            throw new DomainException($"通道不存在：{channelId}");
        if (session.Configuration.TransportKind != ChannelTransportKind.Serial)
            throw new DomainException($"通道 {session.Configuration.Code} 不是 Modbus RTU 串口通道");
        return _rtuConnections.GetOrAdd(
            session.Configuration.Id,
            _ => new ModbusRtuChannelConnection(session.Configuration, _modbusClientFactory));
    }

    /// <summary>
    /// 只读查询指定串口是否已经被运行时占用，连接测试据此避免打开第二个客户端。
    /// </summary>
    public bool IsSerialPortLeased(string portName)
        => !string.IsNullOrWhiteSpace(portName)
            && _rtuConnections.Values.Any(connection =>
                string.Equals(connection.PortName.Trim(), portName.Trim(), StringComparison.OrdinalIgnoreCase)
                && connection.IsLeased);

    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        foreach (var session in _sessions.Values)
            await session.StartAsync(ct);
    }

    public Task<T> ExecuteAsync<T>(
        string channelId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
        => ExecuteAsync(channelId, string.Empty, operation, ct);

    public Task<T> ExecuteAsync<T>(
        string channelId,
        string deviceId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_sessions.TryGetValue(channelId.Trim(), out var session))
            throw new DomainException($"通道不存在：{channelId}");
        return session.ExecuteAsync(deviceId, operation, ct);
    }

    public Task StopAsync(CancellationToken ct = default) => DrainAndStopAsync(ct);

    public async Task DrainAndStopAsync(CancellationToken ct = default)
    {
        foreach (var session in _sessions.Values)
            await session.DrainAndStopAsync(ct);
        foreach (var connection in _rtuConnections.Values)
            await connection.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
        foreach (var connection in _rtuConnections.Values)
            await connection.DisposeAsync();
    }
}

public enum ChannelSessionState
{
    Ready = 1,
    Draining = 2,
    Stopped = 3,
    Disposed = 4
}

/// <summary>
/// 单通道会话。Stop 只进入停止态，不释放信号量，因此同一实例可以再次 Start。
/// </summary>
public sealed class ChannelSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceGates = new(StringComparer.OrdinalIgnoreCase);
    private int _inFlight;
    private int _maxInFlight;
    private int _state = (int)ChannelSessionState.Stopped;

    internal ChannelSession(ChannelEntry configuration) => Configuration = configuration;

    public ChannelEntry Configuration { get; }
    public int InFlight => Volatile.Read(ref _inFlight);
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);
    public ChannelSessionState State => (ChannelSessionState)Volatile.Read(ref _state);

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (State == ChannelSessionState.Disposed)
            throw new ObjectDisposedException(nameof(ChannelSession));
        Interlocked.Exchange(ref _state, (int)ChannelSessionState.Ready);
        return Task.CompletedTask;
    }

    internal async Task<T> ExecuteAsync<T>(
        string deviceId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(State == ChannelSessionState.Disposed, this);
        if (State == ChannelSessionState.Stopped)
            await StartAsync(ct);
        if (State != ChannelSessionState.Ready)
            throw new DomainException($"通道 {Configuration.Code} 当前正在停止");

        var gate = SelectGate(deviceId);
        await gate.WaitAsync(ct);
        try
        {
            if (State != ChannelSessionState.Ready)
                throw new DomainException($"通道 {Configuration.Code} 当前不可用");
            var active = Interlocked.Increment(ref _inFlight);
            UpdateMax(active);
            return await operation(ct);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            gate.Release();
        }
    }

    public async Task DrainAndStopAsync(CancellationToken ct = default)
    {
        if (State == ChannelSessionState.Disposed) return;
        Interlocked.Exchange(ref _state, (int)ChannelSessionState.Draining);
        await _gate.WaitAsync(ct);
        _gate.Release();
        foreach (var gate in _deviceGates.Values)
        {
            await gate.WaitAsync(ct);
            gate.Release();
        }
        Interlocked.Exchange(ref _state, (int)ChannelSessionState.Stopped);
    }

    public ValueTask DisposeAsync()
    {
        if (State == ChannelSessionState.Disposed) return ValueTask.CompletedTask;
        Interlocked.Exchange(ref _state, (int)ChannelSessionState.Disposed);
        _gate.Dispose();
        foreach (var gate in _deviceGates.Values)
            gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private SemaphoreSlim SelectGate(string deviceId)
    {
        if (Configuration.TransportKind != ChannelTransportKind.Tcp || string.IsNullOrWhiteSpace(deviceId))
            return _gate;
        return _deviceGates.GetOrAdd(deviceId.Trim(), _ => new SemaphoreSlim(1, 1));
    }

    private void UpdateMax(int active)
    {
        while (active > Volatile.Read(ref _maxInFlight))
        {
            var current = Volatile.Read(ref _maxInFlight);
            if (active <= current || Interlocked.CompareExchange(ref _maxInFlight, active, current) == current)
                return;
        }
    }
}
