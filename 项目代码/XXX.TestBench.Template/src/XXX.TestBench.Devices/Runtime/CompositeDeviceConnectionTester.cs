using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 按设备驱动键路由连接测试，避免设备编辑器知道具体协议实现类。
/// </summary>
public sealed class CompositeDeviceConnectionTester : IDeviceConnectionTester
{
    private readonly IReadOnlyDictionary<string, IDeviceConnectionTester> _testers;

    public CompositeDeviceConnectionTester(IEnumerable<(string DriverKey, IDeviceConnectionTester Tester)> testers)
    {
        _testers = testers.ToDictionary(
            item => item.DriverKey.Trim(),
            item => item.Tester,
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<DeviceConnectionTestResult> TestAsync(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        CancellationToken ct = default)
    {
        if (_testers.TryGetValue(device.DriverKey?.Trim() ?? string.Empty, out var tester))
            return tester.TestAsync(device, channel, ct);

        var message = "当前设备通信方式没有可用的连接测试器";
        return Task.FromResult(new DeviceConnectionTestResult(
            false,
            false,
            channel.TransportKind.ToString(),
            TimeSpan.Zero,
            device.DriverKey ?? string.Empty,
            DeviceConnectionTestLevel.TransportOnly,
            message,
            null,
            message));
    }
}
