using System.IO.Ports;
using System.Net.Sockets;
using NModbus;
using NModbus.IO;
using NModbus.Serial;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// NModbus 3.x 的 TCP/RTU 创建适配器。
///
/// TCP 连接和串口打开都在这里完成；运行时层只看到 IModbusClient，不会把
/// SerialPort、TcpClient 或第三方异常泄漏到核心业务边界。
/// </summary>
public sealed class NModbusClientFactory : IModbusClientFactory
{
    private readonly ModbusFactory _factory;

    public NModbusClientFactory(ModbusFactory? factory = null)
        => _factory = factory ?? new ModbusFactory();

    public async Task<IModbusClient> CreateTcpAsync(
        string host,
        int port,
        TimeSpan connectTimeout,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new DomainException("Modbus TCP 地址不能为空");
        if (port is < 1 or > 65535)
            throw new DomainException($"Modbus TCP 端口无效：{port}");

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host.Trim(), port).WaitAsync(connectTimeout, ct);
            return new NModbusClientAdapter(_factory.CreateMaster(client));
        }
        catch (TimeoutException)
        {
            client.Dispose();
            throw new DomainException($"Modbus TCP 连接超时：{host.Trim()}:{port}");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<IModbusClient> CreateRtuAsync(
        SerialChannelParameters serial,
        TimeSpan connectTimeout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(serial);
        if (string.IsNullOrWhiteSpace(serial.PortName))
            throw new DomainException("Modbus RTU 串口名称不能为空");
        if (serial.BaudRate <= 0)
            throw new DomainException("Modbus RTU 波特率必须大于 0");
        if (!Enum.TryParse<Parity>(serial.Parity, true, out var parity))
            throw new DomainException($"Modbus RTU 校验方式不受支持：{serial.Parity}");
        if (!Enum.TryParse<StopBits>(serial.StopBits, true, out var stopBits)
            || stopBits == StopBits.None)
            throw new DomainException($"Modbus RTU 停止位不受支持：{serial.StopBits}");

        var port = new SerialPort(serial.PortName.Trim(), serial.BaudRate, parity, serial.DataBits, stopBits);
        try
        {
            // SerialPort.Open 是同步 API，放到线程池并用同一个连接超时保护，避免卡住 UI 线程。
            await Task.Run(port.Open, ct).WaitAsync(connectTimeout, ct);
            var resource = new SerialPortAdapter(port);
            var transport = _factory.CreateRtuTransport(resource);
            transport.Retries = 0;
            return new NModbusClientAdapter(_factory.CreateMaster(transport));
        }
        catch (TimeoutException)
        {
            port.Dispose();
            throw new DomainException($"Modbus RTU 打开串口超时：{serial.PortName}");
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }
}
