using System.Diagnostics;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// Modbus RTU 候选设备测试。
///
/// 如果目标串口已经由活动运行时租用，测试会直接返回“未执行”，绝不打开第二个
/// SerialPort。否则仅打开并释放一次 RTU 传输，不发起点位读取。
/// </summary>
public sealed class ModbusRtuConnectionTester : IDeviceConnectionTester
{
    private readonly IModbusClientFactory _factory;
    private readonly DeviceOperationCoordinator? _operations;
    private readonly Func<string, bool> _isSerialPortLeased;

    public ModbusRtuConnectionTester(
        IModbusClientFactory? factory = null,
        DeviceOperationCoordinator? operations = null,
        Func<string, bool>? isSerialPortLeased = null)
    {
        _factory = factory ?? new NModbusClientFactory();
        _operations = operations;
        _isSerialPortLeased = isSerialPortLeased ?? (_ => false);
    }

    public async Task<DeviceConnectionTestResult> TestAsync(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(channel);
        if (_operations is not null)
        {
            await using var lease = await _operations.EnterDiagnosticsAsync(ct);
            return await TestCoreAsync(device, channel, ct);
        }
        return await TestCoreAsync(device, channel, ct);
    }

    private async Task<DeviceConnectionTestResult> TestCoreAsync(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        CancellationToken ct)
    {
        var serial = channel.Serial;
        var endpoint = $"{serial?.PortName ?? "串口未配置"}，站号 {device.ModbusUnitId?.ToString() ?? "未配置"}";
        var stopwatch = Stopwatch.StartNew();
        if (!string.Equals(device.DriverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase))
            return NotExecuted(endpoint, stopwatch, "当前设备不是 Modbus RTU 驱动");
        if (channel.TransportKind != ChannelTransportKind.Serial || serial is null)
            return NotExecuted(endpoint, stopwatch, "Modbus RTU 必须使用串口通道");
        if (!device.ModbusUnitId.HasValue || device.ModbusUnitId.Value is < 1 or > 247)
            return NotExecuted(endpoint, stopwatch, "Modbus RTU 站号必须在 1-247 范围内");
        if (_isSerialPortLeased(serial.PortName))
        {
            stopwatch.Stop();
            const string message = "当前串口已被运行中的 Modbus RTU 设备占用，未重复打开";
            return new DeviceConnectionTestResult(
                false,
                false,
                endpoint,
                stopwatch.Elapsed,
                DriverKeyCatalog.ModbusRtu,
                DeviceConnectionTestLevel.TransportOnly,
                message,
                null,
                message);
        }

        IModbusClient? client = null;
        try
        {
            var timing = device.Timing ?? new DeviceTimingOptions();
            client = await _factory.CreateRtuAsync(
                serial,
                TimeSpan.FromMilliseconds(timing.ConnectTimeoutMs),
                ct);
            stopwatch.Stop();
            return new DeviceConnectionTestResult(
                true,
                true,
                endpoint,
                stopwatch.Elapsed,
                DriverKeyCatalog.ModbusRtu,
                DeviceConnectionTestLevel.TransportOnly,
                "Modbus RTU 传输连接成功；尚未读取点位",
                null,
                null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failed(endpoint, stopwatch, "Modbus RTU 打开串口超时");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Failed(endpoint, stopwatch, ex.Message);
        }
        finally
        {
            if (client is not null)
                await client.DisposeAsync();
        }
    }

    private static DeviceConnectionTestResult NotExecuted(string endpoint, Stopwatch stopwatch, string message)
    {
        stopwatch.Stop();
        return new DeviceConnectionTestResult(
            false,
            false,
            endpoint,
            stopwatch.Elapsed,
            DriverKeyCatalog.ModbusRtu,
            DeviceConnectionTestLevel.TransportOnly,
            message,
            null,
            message);
    }

    private static DeviceConnectionTestResult Failed(string endpoint, Stopwatch stopwatch, string message)
        => new(
            false,
            true,
            endpoint,
            stopwatch.Elapsed,
            DriverKeyCatalog.ModbusRtu,
            DeviceConnectionTestLevel.TransportOnly,
            "Modbus RTU 传输连接失败",
            null,
            message);
}
