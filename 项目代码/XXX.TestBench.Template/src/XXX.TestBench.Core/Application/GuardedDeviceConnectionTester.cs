using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 为现有协议连接测试器补充统一的手工设备操作安全边界。
/// 协议测试器只负责参数校验和传输层握手，不再各自维护互斥锁。
/// </summary>
public sealed class GuardedDeviceConnectionTester : IDeviceConnectionTester
{
    private readonly DeviceManualOperationGuard _guard;
    private readonly IDeviceConnectionTester _inner;

    public GuardedDeviceConnectionTester(
        DeviceManualOperationGuard guard,
        IDeviceConnectionTester inner)
    {
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<DeviceConnectionTestResult> TestAsync(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        CancellationToken ct = default)
        => _guard.ExecuteAsync(token => _inner.TestAsync(device, channel, token), ct);
}
