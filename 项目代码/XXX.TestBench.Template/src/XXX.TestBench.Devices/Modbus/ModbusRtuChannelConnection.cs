using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// 一个串口通道的 Modbus RTU 连接所有者。
///
/// 同一串口上的多个设备会话只通过这个对象取得客户端，既避免重复打开串口，
/// 也让 ChannelManager 可以在所有会话停止并排空请求后统一关闭资源。
/// </summary>
public sealed class ModbusRtuChannelConnection : IAsyncDisposable
{
    private readonly ChannelEntry _channel;
    private readonly IModbusClientFactory _factory;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private IModbusClient? _client;
    private long _generation;
    private int _disposed;

    public ModbusRtuChannelConnection(ChannelEntry channel, IModbusClientFactory factory)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (_channel.TransportKind != ChannelTransportKind.Serial || _channel.Serial is null)
            throw new DomainException($"通道 {_channel.Code} 不是可用于 Modbus RTU 的串口通道");
    }

    public ChannelEntry Channel => _channel;
    public bool IsOpen => _client is not null;
    public long ConnectionGeneration => Volatile.Read(ref _generation);
    public string PortName => _channel.Serial?.PortName ?? string.Empty;

    /// <summary>
    /// 查询串口是否已经被当前 Modbus RTU 运行时租用。
    /// </summary>
    public bool IsLeased => IsOpen;

    public async Task<IModbusClient> EnsureOpenAsync(TimeSpan connectTimeout, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_client is not null)
                return _client;

            var serial = _channel.Serial
                ?? throw new DomainException($"通道 {_channel.Code} 缺少串口参数");
            var client = await _factory.CreateRtuAsync(serial, connectTimeout, ct);
            _client = client;
            Interlocked.Increment(ref _generation);
            return client;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// 使当前客户端失效。调用者通常在 ChannelManager 的串口串行门内调用。
    /// </summary>
    public async Task InvalidateAsync()
    {
        IModbusClient? client;
        await _lifecycle.WaitAsync();
        try
        {
            client = _client;
            _client = null;
        }
        finally
        {
            _lifecycle.Release();
        }

        if (client is not null)
            await client.DisposeAsync();
    }

    public Task StopAsync() => InvalidateAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await InvalidateAsync();
        _lifecycle.Dispose();
    }
}
