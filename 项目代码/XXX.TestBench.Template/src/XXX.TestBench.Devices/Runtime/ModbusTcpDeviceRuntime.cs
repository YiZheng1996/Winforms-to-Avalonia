using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 单台 Modbus TCP 设备运行时。
///
/// 每台设备根据自己的 ModbusTcp.Host/Port 创建独立 TCP 客户端；即使多个设备
/// 共享一个 TCP 调度通道，也不会把其中一台设备的远端地址用于另一台设备。
/// </summary>
public sealed class ModbusTcpDeviceRuntime : ModbusDeviceRuntimeBase
{
    private readonly IModbusClientFactory _clientFactory;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IModbusClient? _client;
    private long _connectionGeneration;

    public ModbusTcpDeviceRuntime(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        IReadOnlyList<DevicePoint> points,
        IClock clock,
        string revision,
        IModbusClientFactory? clientFactory = null,
        IDeviceEventSink? events = null)
        : base(device, channel, points, clock, revision, events)
    {
        _clientFactory = clientFactory ?? new NModbusClientFactory();
    }

    protected override string ProtocolDisplayName => "Modbus TCP";
    protected override bool HasConnection => _client is not null;
    protected override long CurrentConnectionGeneration => Volatile.Read(ref _connectionGeneration);
    protected override string EndpointText
        => Device.ModbusTcp is { } endpoint
            ? $"{endpoint.Host}:{endpoint.Port}，站号 {Device.ModbusUnitId?.ToString() ?? "未配置"}"
            : "Modbus TCP 端点未配置";

    protected override async Task<IModbusClient> EnsureConnectionAsync(
        TimeSpan connectTimeout,
        CancellationToken ct)
    {
        await _connectionGate.WaitAsync(ct);
        try
        {
            if (_client is not null)
                return _client;
            var endpoint = Device.ModbusTcp
                ?? throw new DomainException("Modbus TCP 缺少设备专属端点");
            _client = await _clientFactory.CreateTcpAsync(endpoint.Host, endpoint.Port, connectTimeout, ct);
            Interlocked.Increment(ref _connectionGeneration);
            return _client;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    protected override async Task InvalidateConnectionAsync()
    {
        IModbusClient? client;
        await _connectionGate.WaitAsync();
        try
        {
            client = _client;
            _client = null;
        }
        finally
        {
            _connectionGate.Release();
        }
        if (client is not null)
            await client.DisposeAsync();
    }

    protected override Task StopConnectionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return InvalidateConnectionAsync();
    }

    public override string ToString() => $"{Name} ({EndpointText})";
}
