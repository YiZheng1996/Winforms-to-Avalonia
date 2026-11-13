using System.Diagnostics;
using S7.Net;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Siemens;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 候选设备的只读 TCP/S7 连接测试，不读取点位、不保存配置、不切换 active revision。
/// </summary>
public sealed class S7DeviceConnectionTester : IDeviceConnectionTester
{
    private readonly IS7PlcClientFactory _clientFactory;
    private readonly DeviceOperationCoordinator? _operations;

    public S7DeviceConnectionTester(
        IS7PlcClientFactory? clientFactory = null,
        DeviceOperationCoordinator? operations = null)
    {
        _clientFactory = clientFactory ?? new S7NetPlusPlcClientFactory();
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
        var endpoint = ResolveEndpoint(device, channel);
        var stopwatch = Stopwatch.StartNew();
        if (channel.TransportKind != ChannelTransportKind.Tcp)
            return Result(false, endpoint.Text, stopwatch.Elapsed, "连接测试失败：西门子 S7 需要 TCP 通道");
        if (!string.Equals(device.DriverKey, DriverKeyCatalog.SiemensS7, StringComparison.OrdinalIgnoreCase))
            return Result(false, endpoint.Text, stopwatch.Elapsed, "连接测试失败：当前设备不是 Siemens S7 驱动");
        if (string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.Port is < 1 or > 65535)
            return Result(false, endpoint.Text, stopwatch.Elapsed, "连接测试失败：候选设备端点不完整");

        var timing = device.Timing ?? new DeviceTimingOptions();
        IS7PlcClient? client = null;
        try
        {
            client = _clientFactory.Create(ResolveCpuType(device.Model), endpoint.Host, endpoint.Port,
                endpoint.Rack, endpoint.Slot, timing.RequestTimeoutMs);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timing.ConnectTimeoutMs);
            await client.OpenAsync(timeout.Token);
            stopwatch.Stop();
            return new DeviceConnectionTestResult(
                true,
                true,
                endpoint.Text,
                stopwatch.Elapsed,
                DriverKeyCatalog.SiemensS7,
                DeviceConnectionTestLevel.TransportOnly,
                "TCP/S7 会话建立成功",
                client.MaxPduSize,
                null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Result(false, endpoint.Text, stopwatch.Elapsed,
                $"连接测试超时（{timing.ConnectTimeoutMs} ms）");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Result(false, endpoint.Text, stopwatch.Elapsed,
                $"连接测试失败：{ex.Message}");
        }
        finally
        {
            if (client is not null)
                await client.DisposeAsync();
        }
    }

    private static DeviceConnectionTestResult Result(
        bool ok,
        string endpoint,
        TimeSpan elapsed,
        string error)
        => new(
            ok,
            true,
            endpoint,
            elapsed,
            DriverKeyCatalog.SiemensS7,
            DeviceConnectionTestLevel.TransportOnly,
            ok ? "TCP/S7 会话建立成功" : error,
            null,
            ok ? null : error);

    private static (string Host, int Port, int Rack, int Slot, string Text) ResolveEndpoint(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel)
    {
        var options = device.SiemensS7;
        var host = options?.Host?.Trim();
        if (string.IsNullOrWhiteSpace(host)) host = device.Address?.Trim();
        if (string.IsNullOrWhiteSpace(host)) host = channel.Tcp?.Host?.Trim();
        var port = options?.Port is > 0 and <= 65535
            ? options.Port
            : channel.Tcp?.Port is > 0 and <= 65535 ? channel.Tcp.Port : 102;
        var rack = options?.Rack ?? 0;
        var slot = options?.Slot ?? 0;
        var text = $"{host}:{port} rack={rack} slot={slot}";
        return (host ?? string.Empty, port, rack, slot, text);
    }

    private static CpuType ResolveCpuType(string? model)
    {
        var value = model ?? string.Empty;
        if (value.Contains("1500", StringComparison.OrdinalIgnoreCase)) return CpuType.S71500;
        if (value.Contains("200", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("1200", StringComparison.OrdinalIgnoreCase)) return CpuType.S7200Smart;
        return CpuType.S71200;
    }
}
