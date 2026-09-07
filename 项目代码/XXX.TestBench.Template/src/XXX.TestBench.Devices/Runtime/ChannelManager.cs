using System.Collections.Concurrent;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 通道资源管理器。每个通道只允许一个协议请求进入，读写共享同一把门。
/// </summary>
public sealed class ChannelManager : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<string, ChannelSession> _sessions;
    private int _disposed;

    public ChannelManager(IEnumerable<ChannelEntry> channels)
    {
        _sessions = channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Id))
            .GroupBy(channel => channel.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new ChannelSession(group.First()),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ChannelSession> Sessions => _sessions.Values.ToArray();

    public Task<T> ExecuteAsync<T>(
        string channelId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_sessions.TryGetValue(channelId.Trim(), out var session))
            throw new DomainException($"通道不存在：{channelId}");
        return session.ExecuteAsync(operation, ct);
    }

    public Task StopAsync(CancellationToken ct = default)
        => Task.WhenAll(_sessions.Values.Select(session => session.StopAsync(ct)));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
    }
}
/// <summary>
/// 单个通道的串行调度会话。
/// </summary>
public sealed class ChannelSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _inFlight;
    private int _maxInFlight;
    private int _stopping;

    internal ChannelSession(ChannelEntry configuration) => Configuration = configuration;

    public ChannelEntry Configuration { get; }
    public int InFlight => Volatile.Read(ref _inFlight);
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
            var active = Interlocked.Increment(ref _inFlight);
            UpdateMax(active);
            return await operation(ct);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _stopping, 1);
        await _gate.WaitAsync(ct);
        _gate.Release();
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopping, 1);
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private void UpdateMax(int active)
    {
        while (active > Volatile.Read(ref _maxInFlight)
               && Interlocked.CompareExchange(ref _maxInFlight, active, Volatile.Read(ref _maxInFlight)) != Volatile.Read(ref _maxInFlight))
        {
        }
    }
}
