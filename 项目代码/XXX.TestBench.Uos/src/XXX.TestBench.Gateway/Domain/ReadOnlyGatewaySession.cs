namespace XXX.TestBench.Gateway.Domain;

public sealed class ReadOnlyGatewaySession
{
    private long _connectionGeneration;
    private int _connected;

    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);
    public bool IsConnected => Volatile.Read(ref _connected) == 1;
    public bool WritesEnabled => false;

    public void MarkConnected()
    {
        Interlocked.Increment(ref _connectionGeneration);
        Volatile.Write(ref _connected, 1);
    }

    public void MarkDisconnected()
    {
        Volatile.Write(ref _connected, 0);
    }

    public GatewaySample<T> Accept<T>(string pointId, T value, DateTimeOffset timestamp)
    {
        if (!IsConnected)
            return new GatewaySample<T>(pointId, default, DataQuality.Bad, timestamp, ConnectionGeneration, "Gateway 未连接");

        return new GatewaySample<T>(pointId, value, DataQuality.Good, timestamp, ConnectionGeneration);
    }

    public GatewaySample<T> Unknown<T>(string pointId) =>
        GatewaySample<T>.Unknown(pointId, ConnectionGeneration);
}
