using S7.Net;

namespace XXX.TestBench.Devices.Siemens;

/// <summary>
/// S7NetPlus 的最小适配边界。运行时和测试替身不再直接依赖 Plc 实例。
/// </summary>
public interface IS7PlcClient : IAsyncDisposable
{
    bool IsConnected { get; }
    int MaxPduSize { get; }
    Task OpenAsync(CancellationToken ct = default);
    Task<byte[]> ReadBytesAsync(DataType area, int dbNumber, int startByte, int count, CancellationToken ct = default);
    Task WriteBytesAsync(DataType area, int dbNumber, int startByte, byte[] bytes, CancellationToken ct = default);
    Task WriteBitAsync(DataType area, int dbNumber, int startByte, int bitIndex, bool value, CancellationToken ct = default);
}

public interface IS7PlcClientFactory
{
    IS7PlcClient Create(CpuType cpuType, string host, int port, int rack, int slot, int requestTimeoutMs);
}

/// <summary>
/// 默认 S7NetPlus 客户端工厂；这是唯一直接 new Plc 的位置。
/// </summary>
public sealed class S7NetPlusPlcClientFactory : IS7PlcClientFactory
{
    public IS7PlcClient Create(CpuType cpuType, string host, int port, int rack, int slot, int requestTimeoutMs)
        => new S7NetPlusPlcClient(cpuType, host, port, rack, slot, requestTimeoutMs);
}

public sealed class S7NetPlusPlcClient : IS7PlcClient
{
    private readonly Plc _plc;

    public S7NetPlusPlcClient(CpuType cpuType, string host, int port, int rack, int slot, int requestTimeoutMs)
    {
        _plc = new Plc(cpuType, host, port, checked((short)rack), checked((short)slot))
        {
            ReadTimeout = requestTimeoutMs,
            WriteTimeout = requestTimeoutMs
        };
    }

    public bool IsConnected => _plc.IsConnected;
    public int MaxPduSize => _plc.MaxPDUSize;

    public Task OpenAsync(CancellationToken ct = default) => _plc.OpenAsync(ct);

    public Task<byte[]> ReadBytesAsync(DataType area, int dbNumber, int startByte, int count, CancellationToken ct = default)
        => _plc.ReadBytesAsync(area, dbNumber, startByte, count, ct);

    public Task WriteBytesAsync(DataType area, int dbNumber, int startByte, byte[] bytes, CancellationToken ct = default)
        => _plc.WriteBytesAsync(area, dbNumber, startByte, bytes, ct);

    public Task WriteBitAsync(DataType area, int dbNumber, int startByte, int bitIndex, bool value, CancellationToken ct = default)
        => _plc.WriteBitAsync(area, dbNumber, startByte, bitIndex, value, ct);

    public ValueTask DisposeAsync()
    {
        try { _plc.Close(); } catch { }
        try { ((IDisposable)_plc).Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}
