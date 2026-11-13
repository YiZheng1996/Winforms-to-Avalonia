using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 使用给定设备和通信通道执行连接检查。
/// </summary>
public interface IDeviceConnectionTester
{
    /// <summary>
    /// 测试当前设备配置能否通过所选通道建立连接。
    /// </summary>
    /// <param name="device">待检查的设备配置。</param>
    /// <param name="channel">设备引用的通信通道。</param>
    /// <param name="ct">取消本次连接检查。</param>
    /// <returns>连接状态及失败原因。</returns>
    Task<DeviceConnectionTestResult> TestAsync(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        CancellationToken ct = default);
}
