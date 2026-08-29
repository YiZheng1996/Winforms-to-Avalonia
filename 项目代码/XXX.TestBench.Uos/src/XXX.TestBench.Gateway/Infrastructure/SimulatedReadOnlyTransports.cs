using System.Collections.ObjectModel;
using XXX.TestBench.Gateway.Domain;

namespace XXX.TestBench.Gateway.Infrastructure;

public sealed class SimulatedS7ReadOnlyTransport : IReadOnlyS7Transport
{
    private readonly IReadOnlyDictionary<string, byte[]> _values;
    private int _connected;
    private long _connectionGeneration;

    public SimulatedS7ReadOnlyTransport(IReadOnlyDictionary<string, byte[]>? values = null)
    {
        _values = new ReadOnlyDictionary<string, byte[]>(
            (values ?? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["VD200"] = [0x42, 0x2A, 0x00, 0x00], // 42.5f, Siemens big-endian byte order
                ["V0.0"] = [1]
            }).ToDictionary(
                x => x.Key,
                x => x.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase));
    }

    public string EndpointId => "Simulated-S7";
    public bool IsConnected => Volatile.Read(ref _connected) == 1;
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _connected, 1) == 0)
            Interlocked.Increment(ref _connectionGeneration);
        return Task.CompletedTask;
    }

    public Task<S7RawReadResult> ReadMemoryAsync(
        string address,
        int byteCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (byteCount is < 1 or > 2_000)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        if (!IsConnected)
        {
            return Task.FromResult(new S7RawReadResult(
                address,
                [],
                DataQuality.Bad,
                DateTimeOffset.UtcNow,
                ConnectionGeneration,
                "仿真 S7 未连接"));
        }

        var data = new byte[byteCount];
        if (_values.TryGetValue(address, out var configured))
            Array.Copy(configured, data, Math.Min(configured.Length, data.Length));

        return Task.FromResult(new S7RawReadResult(
            address,
            data,
            DataQuality.Good,
            DateTimeOffset.UtcNow,
            ConnectionGeneration));
    }

    public Task DisconnectAsync()
    {
        Volatile.Write(ref _connected, 0);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class SimulatedModbusReadOnlyTransport : IReadOnlyModbusTransport
{
    private readonly IReadOnlyDictionary<ushort, ushort> _registers;
    private int _connected;
    private long _connectionGeneration;

    public SimulatedModbusReadOnlyTransport(IReadOnlyDictionary<ushort, ushort>? registers = null)
    {
        _registers = new ReadOnlyDictionary<ushort, ushort>(
            (registers ?? new Dictionary<ushort, ushort> { [0] = 1234, [1] = 5678 })
            .ToDictionary(x => x.Key, x => x.Value));
    }

    public string TransportId => "Simulated-Modbus";
    public bool IsConnected => Volatile.Read(ref _connected) == 1;
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _connected, 1) == 0)
            Interlocked.Increment(ref _connectionGeneration);
        return Task.CompletedTask;
    }

    public Task<ModbusRegisterReadResult> ReadRegistersAsync(
        ModbusRegisterReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.RegisterCount is 0 or > 125)
            throw new ArgumentOutOfRangeException(nameof(request));

        if (!IsConnected)
        {
            return Task.FromResult(new ModbusRegisterReadResult(
                request,
                [],
                DataQuality.Bad,
                DateTimeOffset.UtcNow,
                ConnectionGeneration,
                "仿真 Modbus 未连接"));
        }

        var values = new ushort[request.RegisterCount];
        for (var i = 0; i < values.Length; i++)
            values[i] = _registers.TryGetValue((ushort)(request.StartAddress + i), out var value) ? value : (ushort)0;

        return Task.FromResult(new ModbusRegisterReadResult(
            request,
            values,
            DataQuality.Good,
            DateTimeOffset.UtcNow,
            ConnectionGeneration));
    }

    public Task DisconnectAsync()
    {
        Volatile.Write(ref _connected, 0);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
