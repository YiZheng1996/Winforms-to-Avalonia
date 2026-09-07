using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Devices.Drivers;

/// <summary>
/// 仿真驱动描述。只校验仿真配置，不访问设备。
/// </summary>
public sealed class SimulationDriverDescriptor : IDeviceDriverDescriptor
{
    private static readonly IReadOnlySet<DevicePointDataType> Supported = new HashSet<DevicePointDataType>
    {
        DevicePointDataType.Boolean,
        DevicePointDataType.Bool,
        DevicePointDataType.Decimal,
        DevicePointDataType.Int32,
        DevicePointDataType.Int16,
        DevicePointDataType.UInt16,
        DevicePointDataType.UInt32,
        DevicePointDataType.Float32,
        DevicePointDataType.String
    };

    public string DriverKey => DriverKeyCatalog.Simulation;
    public string DisplayName => "仿真";
    public bool IsImplemented => true;
    public IReadOnlySet<DevicePointDataType> SupportedDataTypes => Supported;

    public IReadOnlyList<DriverValidationIssue> ValidateChannel(ChannelEntry channel)
    {
        var issues = new List<DriverValidationIssue>();
        if (channel.TransportKind != ChannelTransportKind.Simulation)
            issues.Add(new("transportKind", "simulation 驱动必须使用仿真通道"));
        if (channel.Simulation is null)
            issues.Add(new("simulation", "simulation 驱动必须配置仿真参数"));
        return issues;
    }

    public IReadOnlyList<DriverValidationIssue> ValidateDevice(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel)
    {
        var issues = new List<DriverValidationIssue>();
        if (!string.Equals(device.DriverKey, DriverKey, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("driverKey", "设备驱动键与描述不匹配"));
        return issues;
    }

    public IReadOnlyList<DriverValidationIssue> ValidatePoint(
        PointsConfig.PointEntry point,
        DeviceConfig.DeviceEntry device)
    {
        var issues = new List<DriverValidationIssue>();
        if (string.IsNullOrWhiteSpace(point.Address))
            issues.Add(new("address", "仿真逻辑地址不能为空"));
        if (!Supported.Contains(point.RawDataTypeKind))
            issues.Add(new("rawDataType", $"仿真驱动不支持原始数据类型：{point.RawDataType}"));
        if (point.AddressDefinition is { } address
            && (!string.IsNullOrWhiteSpace(address.Area)
                || address.Offset.HasValue
                || address.BitIndex.HasValue
                || address.DbNumber.HasValue
                || address.ByteOffset.HasValue
                || address.BitOffset.HasValue))
            issues.Add(new("addressDefinition", "仿真点位只能使用 LogicalAddress"));
        return issues;
    }

    public string NormalizeAddress(PointsConfig.PointEntry point)
        => (point.AddressDefinition?.ToCanonical(point.Address) ?? point.Address).Trim();
}
