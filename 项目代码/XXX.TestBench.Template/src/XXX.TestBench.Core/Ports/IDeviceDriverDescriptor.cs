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
    /// <summary>
    /// 驱动允许使用的传输类型。界面和核心校验共用此集合，避免把设备驱动误挂到错误通道。
    /// </summary>
    IReadOnlySet<ChannelTransportKind> SupportedTransports { get; }
    IReadOnlyList<DeviceModelDescriptor> DeviceModels { get; }
    IReadOnlySet<DevicePointDataType> SupportedDataTypes { get; }
    IReadOnlyList<DriverValidationIssue> ValidateChannel(ChannelEntry channel);
    IReadOnlyList<DriverValidationIssue> ValidateDevice(DeviceConfig.DeviceEntry device, ChannelEntry channel);
    IReadOnlyList<DriverValidationIssue> ValidatePoint(PointsConfig.PointEntry point, DeviceConfig.DeviceEntry device);
    string NormalizeAddress(PointsConfig.PointEntry point);
}

/// <summary>
/// 驱动提供的受限设备系列。界面只允许从该集合选择，地址示例也由同一来源提供。
/// </summary>
public sealed record DeviceModelDescriptor(
    string Key,
    string DisplayName,
    string AddressWatermark,
    string AddressHint);

public sealed record DriverValidationIssue(string Path, string Message);
