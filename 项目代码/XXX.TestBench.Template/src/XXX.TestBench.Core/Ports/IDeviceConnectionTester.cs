using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

public interface IDeviceConnectionTester
{
    Task<DeviceConnectionTestResult> TestAsync(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        CancellationToken ct = default);
}
