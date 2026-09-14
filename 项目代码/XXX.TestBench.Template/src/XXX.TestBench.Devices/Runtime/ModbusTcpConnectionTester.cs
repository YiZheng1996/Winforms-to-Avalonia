using System.Diagnostics;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// Modbus TCP 候选设备测试。
///
/// 首版连接测试只建立并释放 TCP/Modbus 传输，不读取点位；因此成功提示明确
/// 表示“传输层成功”，不会让客户误以为已经完成点位地址验证。
/// </summary>
public sealed class ModbusTcpConnectionTester : IDeviceConnectionTester
{
    private readonly IModbusClientFactory _factory;
    private readonly DeviceOperationCoordinator? _operations;

    public ModbusTcpConnectionTester(
        IModbusClientFactory? factory = null,
        DeviceOperationCoordinator? operations = null)
    {
        _factory = factory ?? new NModbusClientFactory();
        _operations = operations;
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
        var endpoint = ResolveEndpoint(device);
        var stopwatch = Stopwatch.StartNew();
        if (!string.Equals(device.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase))
            return NotExecuted(endpoint, stopwatch, "当前设备不是 Modbus TCP 驱动");
        if (channel.TransportKind != ChannelTransportKind.Tcp)
            return NotExecuted(endpoint, stopwatch, "Modbus TCP 必须使用 TCP 通道");
        if (device.ModbusTcp is null || string.IsNullOrWhiteSpace(device.ModbusTcp.Host)
            || device.ModbusTcp.Port is < 1 or > 65535)
            return NotExecuted(endpoint, stopwatch, "Modbus TCP 设备端点不完整");
        if (!device.ModbusUnitId.HasValue || device.ModbusUnitId.Value is < 1 or > 247)
            return NotExecuted(endpoint, stopwatch, "Modbus TCP 站号必须在 1-247 范围内");

        IModbusClient? client = null;
        try
        {
            var timing = device.Timing ?? new DeviceTimingOptions();
            client = await _factory.CreateTcpAsync(
                device.ModbusTcp.Host,
                device.ModbusTcp.Port,
                TimeSpan.FromMilliseconds(timing.ConnectTimeoutMs),
                ct);
            stopwatch.Stop();
            return new DeviceConnectionTestResult(
                true,
                true,
                endpoint,
                stopwatch.Elapsed,
                DriverKeyCatalog.ModbusTcp,
                DeviceConnectionTestLevel.TransportOnly,
                "Modbus TCP 传输连接成功；尚未读取点位",
                null,
                null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failed(endpoint, stopwatch, "Modbus TCP 连接超时");
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

    private static string ResolveEndpoint(DeviceConfig.DeviceEntry device)
        => device.ModbusTcp is { } endpoint
            ? $"{endpoint.Host}:{endpoint.Port}，站号 {device.ModbusUnitId?.ToString() ?? "未配置"}"
            : "Modbus TCP 端点未配置";

    private static DeviceConnectionTestResult NotExecuted(
        string endpoint,
        Stopwatch stopwatch,
        string message)
    {
        stopwatch.Stop();
        return new DeviceConnectionTestResult(
            false,
            false,
            endpoint,
            stopwatch.Elapsed,
            DriverKeyCatalog.ModbusTcp,
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
            DriverKeyCatalog.ModbusTcp,
            DeviceConnectionTestLevel.TransportOnly,
            "Modbus TCP 传输连接失败",
            null,
            message);
}
