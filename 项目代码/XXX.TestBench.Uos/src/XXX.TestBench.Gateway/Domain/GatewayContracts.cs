namespace XXX.TestBench.Gateway.Domain;

public enum DataQuality
{
    Unknown,
    Good,
    Uncertain,
    Bad
}

public enum GatewayLogLevel
{
    Trace,
    Information,
    Warning,
    Error
}

public sealed record GatewayLogEntry(
    DateTimeOffset Timestamp,
    GatewayLogLevel Level,
    string Component,
    string Message,
    IReadOnlyDictionary<string, string?> Properties,
    string? Exception = null);

public interface IGatewayLogSink
{
    void Write(GatewayLogEntry entry);
}

public sealed class NullGatewayLogSink : IGatewayLogSink
{
    public static NullGatewayLogSink Instance { get; } = new();

    private NullGatewayLogSink()
    {
    }

    public void Write(GatewayLogEntry entry)
    {
    }
}

public enum ModbusRegisterArea
{
    HoldingRegisters,
    InputRegisters
}

public sealed record GatewaySample<T>(
    string PointId,
    T? Value,
    DataQuality Quality,
    DateTimeOffset Timestamp,
    long ConnectionGeneration,
    string? Diagnostic = null)
{
    public static GatewaySample<T> Unknown(string pointId, long generation = 0) =>
        new(pointId, default, DataQuality.Unknown, DateTimeOffset.UtcNow, generation);
}

public sealed record S7RawReadResult(
    string Address,
    byte[] Data,
    DataQuality Quality,
    DateTimeOffset Timestamp,
    long ConnectionGeneration,
    string? Diagnostic = null);

public sealed record ModbusRegisterReadRequest(
    byte UnitId,
    ushort StartAddress,
    ushort RegisterCount,
    ModbusRegisterArea Area = ModbusRegisterArea.HoldingRegisters);

public sealed record ModbusRegisterReadResult(
    ModbusRegisterReadRequest Request,
    ushort[] Registers,
    DataQuality Quality,
    DateTimeOffset Timestamp,
    long ConnectionGeneration,
    string? Diagnostic = null);

public sealed record GatewayPointSample(
    string PointId,
    object? Value,
    GatewayPointValueType ValueType,
    DataQuality Quality,
    DateTimeOffset Timestamp,
    long ConnectionGeneration,
    string? Diagnostic = null);

public sealed record GatewayPollSnapshot(
    DateTimeOffset Timestamp,
    bool IsSimulated,
    bool IsHealthy,
    IReadOnlyList<GatewayPointSample> Points)
{
    public GatewayPointSample? Find(string pointId) =>
        Points.FirstOrDefault(x => string.Equals(x.PointId, pointId, StringComparison.Ordinal));
}

public interface IReadOnlyS7Transport : IAsyncDisposable
{
    string EndpointId { get; }
    bool IsConnected { get; }
    long ConnectionGeneration { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task<S7RawReadResult> ReadMemoryAsync(string address, int byteCount, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}

public interface IReadOnlyModbusTransport : IAsyncDisposable
{
    string TransportId { get; }
    bool IsConnected { get; }
    long ConnectionGeneration { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task<ModbusRegisterReadResult> ReadRegistersAsync(ModbusRegisterReadRequest request, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}
