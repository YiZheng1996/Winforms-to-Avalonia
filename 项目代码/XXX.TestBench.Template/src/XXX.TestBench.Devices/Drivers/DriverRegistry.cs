using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Devices.Drivers;

/// <summary>
/// 编译期驱动描述注册表。第一版只实现仿真驱动，其余键显式登记为未实现，禁止伪装成可用。
/// </summary>
public sealed class DriverRegistry
{
    private readonly IReadOnlyDictionary<string, IDeviceDriverDescriptor> _descriptors;

    public DriverRegistry(IEnumerable<IDeviceDriverDescriptor> descriptors)
    {
        _descriptors = descriptors
            .GroupBy(descriptor => descriptor.DriverKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IDeviceDriverDescriptor> Descriptors => _descriptors.Values.ToArray();

    public bool TryGet(string driverKey, out IDeviceDriverDescriptor descriptor)
        => _descriptors.TryGetValue(driverKey.Trim(), out descriptor!);

    public static DriverRegistry CreateDefault()
        => new(new IDeviceDriverDescriptor[]
        {
            new SimulationDriverDescriptor(),
            new UnsupportedDriverDescriptor("modbus-rtu", "Modbus RTU"),
            new UnsupportedDriverDescriptor("modbus-tcp", "Modbus TCP"),
            new UnsupportedDriverDescriptor("siemens-s7", "西门子 S7")
        });
}
internal sealed class UnsupportedDriverDescriptor(string driverKey, string displayName) : IDeviceDriverDescriptor
{
    public string DriverKey { get; } = driverKey;
    public string DisplayName { get; } = displayName;
    public bool IsImplemented => false;
    public IReadOnlySet<XXX.TestBench.Core.Domain.Devices.DevicePointDataType> SupportedDataTypes
        => new HashSet<XXX.TestBench.Core.Domain.Devices.DevicePointDataType>();

    public IReadOnlyList<DriverValidationIssue> ValidateChannel(XXX.TestBench.Core.Configuration.ChannelEntry channel)
        => Array.Empty<DriverValidationIssue>();

    public IReadOnlyList<DriverValidationIssue> ValidateDevice(
        XXX.TestBench.Core.Configuration.DeviceConfig.DeviceEntry device,
        XXX.TestBench.Core.Configuration.ChannelEntry channel)
        => Array.Empty<DriverValidationIssue>();

    public IReadOnlyList<DriverValidationIssue> ValidatePoint(
        XXX.TestBench.Core.Configuration.PointsConfig.PointEntry point,
        XXX.TestBench.Core.Configuration.DeviceConfig.DeviceEntry device)
        => Array.Empty<DriverValidationIssue>();

    public string NormalizeAddress(XXX.TestBench.Core.Configuration.PointsConfig.PointEntry point)
        => point.Address.Trim();
}
