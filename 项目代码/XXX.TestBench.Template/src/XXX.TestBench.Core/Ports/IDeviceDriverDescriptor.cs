using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 驱动描述合同。只负责能力和配置/地址校验，不执行硬件 I/O。
/// </summary>
public interface IDeviceDriverDescriptor
{
    string DriverKey { get; }
    string DisplayName { get; }
    bool IsImplemented { get; }
    IReadOnlySet<DevicePointDataType> SupportedDataTypes { get; }
    IReadOnlyList<DriverValidationIssue> ValidateChannel(ChannelEntry channel);
    IReadOnlyList<DriverValidationIssue> ValidateDevice(DeviceConfig.DeviceEntry device, ChannelEntry channel);
    IReadOnlyList<DriverValidationIssue> ValidatePoint(PointsConfig.PointEntry point, DeviceConfig.DeviceEntry device);
    string NormalizeAddress(PointsConfig.PointEntry point);
}

public sealed record DriverValidationIssue(string Path, string Message);
