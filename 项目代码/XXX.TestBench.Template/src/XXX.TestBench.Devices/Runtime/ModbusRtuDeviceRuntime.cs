using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 单台 Modbus RTU 设备运行时。
///
/// 运行时只持有“自己的站号和点位”，串口客户端由 ModbusRtuChannelConnection
/// 统一持有；所有请求仍由 ChannelManager 的串口门串行化，保证 RTU 帧不会交叉。
/// </summary>
public sealed class ModbusRtuDeviceRuntime : ModbusDeviceRuntimeBase
{
    private readonly ModbusRtuChannelConnection _connection;

    public ModbusRtuDeviceRuntime(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        IReadOnlyList<DevicePoint> points,
        IClock clock,
        string revision,
        ModbusRtuChannelConnection connection,
        IDeviceEventSink? events = null)
        : base(device, channel, points, clock, revision, events)
        => _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    protected override string ProtocolDisplayName => "Modbus RTU";
    protected override bool HasConnection => _connection.IsOpen;
    protected override long CurrentConnectionGeneration => _connection.ConnectionGeneration;
    protected override string EndpointText
        => $"{_connection.PortName}，站号 {Device.ModbusUnitId?.ToString() ?? "未配置"}";

    protected override Task<IModbusClient> EnsureConnectionAsync(
        TimeSpan connectTimeout,
        CancellationToken ct)
        => _connection.EnsureOpenAsync(connectTimeout, ct);

    protected override Task InvalidateConnectionAsync()
        => _connection.InvalidateAsync();

    protected override Task StopConnectionAsync(CancellationToken ct)
    {
        // 串口是通道共享资源；这里只响应取消，实际关闭由 ChannelManager 排空所有设备后完成。
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override string ToString() => $"{Name} ({EndpointText})";
}
