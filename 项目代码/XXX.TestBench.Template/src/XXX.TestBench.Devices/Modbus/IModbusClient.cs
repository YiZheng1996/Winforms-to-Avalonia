using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// 运行时使用的最小 Modbus 主站边界。
///
/// 设备会话不直接依赖 NModbus，所有操作都必须带请求超时和取消令牌。
/// 发生超时/取消后，实现必须使当前连接失效，调用方不得复用该连接。
/// </summary>
public interface IModbusClient : IAsyncDisposable
{
    Task<bool[]> ReadCoilsAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct);
    Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct);
    Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct);
    Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct);
    Task WriteSingleCoilAsync(byte unitId, ushort address, bool value, TimeSpan requestTimeout, CancellationToken ct);
    Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, TimeSpan requestTimeout, CancellationToken ct);
    Task WriteMultipleRegistersAsync(byte unitId, ushort address, ushort[] values, TimeSpan requestTimeout, CancellationToken ct);
}

/// <summary>
/// Modbus TCP/RTU 连接创建边界，便于软件模拟和集成测试注入假客户端。
/// </summary>
public interface IModbusClientFactory
{
    Task<IModbusClient> CreateTcpAsync(string host, int port, TimeSpan connectTimeout, CancellationToken ct);
    Task<IModbusClient> CreateRtuAsync(SerialChannelParameters serial, TimeSpan connectTimeout, CancellationToken ct);
}
