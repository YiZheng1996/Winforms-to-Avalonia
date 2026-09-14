using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Devices.Drivers;

/// <summary>
/// 编译期驱动描述注册表。这里只注册实际设备驱动；仿真由设备级运行模式决定，不再作为驱动选项。
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
            new ModbusRtuDriverDescriptor(),
            new ModbusTcpDriverDescriptor(),
            new SiemensS7DriverDescriptor()
        });
}
internal sealed class UnsupportedDriverDescriptor(
    string driverKey,
    string displayName,
    string modelKey,
    string modelDisplayName) : IDeviceDriverDescriptor
{
    public string DriverKey { get; } = driverKey;
    public string DisplayName { get; } = displayName;
    public bool IsImplemented => false;
    public IReadOnlySet<XXX.TestBench.Core.Configuration.ChannelTransportKind> SupportedTransports
        => new HashSet<XXX.TestBench.Core.Configuration.ChannelTransportKind>();
    public IReadOnlyList<DeviceModelDescriptor> DeviceModels { get; } =
    [
        new(modelKey, modelDisplayName, "如：40001 或 0",
            "Modbus 地址可填写设备手册寄存器地址（如 40001）或零基协议偏移（如 0）；具体规则以设备手册为准。")
    ];
    public IReadOnlySet<XXX.TestBench.Core.Domain.Devices.DevicePointDataType> SupportedDataTypes
        => new HashSet<XXX.TestBench.Core.Domain.Devices.DevicePointDataType>();

    public IReadOnlyList<DriverValidationIssue> ValidateChannel(XXX.TestBench.Core.Configuration.ChannelEntry channel)
        => Array.Empty<DriverValidationIssue>();

    public IReadOnlyList<DriverValidationIssue> ValidateDevice(
        XXX.TestBench.Core.Configuration.DeviceConfig.DeviceEntry device,
        XXX.TestBench.Core.Configuration.ChannelEntry channel)
        => DeviceModels.Any(model => string.Equals(model.Key, device.Model, StringComparison.OrdinalIgnoreCase))
            ? Array.Empty<DriverValidationIssue>()
            : new[] { new DriverValidationIssue("model", $"请选择 {modelDisplayName}") };

    public IReadOnlyList<DriverValidationIssue> ValidatePoint(
        XXX.TestBench.Core.Configuration.PointsConfig.PointEntry point,
        XXX.TestBench.Core.Configuration.DeviceConfig.DeviceEntry device)
        => Array.Empty<DriverValidationIssue>();

    public string NormalizeAddress(XXX.TestBench.Core.Configuration.PointsConfig.PointEntry point)
        => point.Address.Trim();
}
