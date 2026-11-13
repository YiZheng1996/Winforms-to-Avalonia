using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices.Drivers;

/// <summary>
/// 标准 Modbus TCP 驱动描述器。
///
/// 描述器只负责“能不能配置”，实际 TCP 连接和功能码调用由 ModbusTcpDeviceRuntime
/// 完成；这样 UI、导入校验和运行时使用的是同一套地址/类型契约。
/// </summary>
public sealed class ModbusTcpDriverDescriptor : IDeviceDriverDescriptor
{
    public string DriverKey => DriverKeyCatalog.ModbusTcp;
    public string DisplayName => "Modbus TCP";
    public bool IsImplemented => true;
    public IReadOnlySet<ChannelTransportKind> SupportedTransports { get; } =
        new HashSet<ChannelTransportKind> { ChannelTransportKind.Tcp };
    public IReadOnlySet<DevicePointDataType> SupportedDataTypes => ModbusTypeCapabilities.SupportedTypes;
    public IReadOnlyList<DeviceModelDescriptor> DeviceModels { get; } =
    [
        new("GENERIC_MODBUS_TCP", "通用 Modbus TCP 设备", "如：HR:0、C:0、40001",
            "地址首选 C:0、DI:0、HR:0、IR:0；兼容明确功能区手册地址 00001/10001/30001/40001。")
    ];

    public IReadOnlyList<DriverValidationIssue> ValidateChannel(ChannelEntry channel)
        => channel.TransportKind == ChannelTransportKind.Tcp
            ? Array.Empty<DriverValidationIssue>()
            : new[] { new DriverValidationIssue("transportKind", "Modbus TCP 当前只接受 TCP 通道") };

    public IReadOnlyList<DriverValidationIssue> ValidateDevice(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel)
    {
        var issues = new List<DriverValidationIssue>();
        if (!string.Equals(device.DriverKey, DriverKey, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("driverKey", "设备驱动键与 Modbus TCP 描述不匹配"));
        if (!DeviceModels.Any(model => string.Equals(model.Key, device.Model, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("model", "请选择通用 Modbus TCP 设备系列"));
        if (device.DeviceMode == DeviceMode.Hardware && device.ModbusTcp is null)
            issues.Add(new("modbusTcp", "硬件 Modbus TCP 必须配置设备专属端点"));
        if (!device.ModbusUnitId.HasValue || device.ModbusUnitId.Value is < 1 or > 247)
            issues.Add(new("modbusUnitId", "Modbus TCP 站号必须在 1-247 范围内，首版不支持广播站号 0"));
        if (device.SiemensS7 is not null)
            issues.Add(new("siemensS7", "Modbus TCP 设备不能同时配置 Siemens S7 端点"));
        return issues;
    }

    public IReadOnlyList<DriverValidationIssue> ValidatePoint(
        PointsConfig.PointEntry point,
        DeviceConfig.DeviceEntry device)
        => ModbusPointValidation.Validate(point, device, DriverKey);

    public string NormalizeAddress(PointsConfig.PointEntry point)
        => ModbusPointValidation.NormalizeAddress(point);
}

/// <summary>
/// Modbus RTU 驱动描述器。
/// </summary>
public sealed class ModbusRtuDriverDescriptor : IDeviceDriverDescriptor
{
    public string DriverKey => DriverKeyCatalog.ModbusRtu;
    public string DisplayName => "Modbus RTU";
    public bool IsImplemented => true;
    public IReadOnlySet<ChannelTransportKind> SupportedTransports { get; } =
        new HashSet<ChannelTransportKind> { ChannelTransportKind.Serial };
    public IReadOnlySet<DevicePointDataType> SupportedDataTypes => ModbusTypeCapabilities.SupportedTypes;
    public IReadOnlyList<DeviceModelDescriptor> DeviceModels { get; } =
    [
        new("GENERIC_MODBUS_RTU", "通用 Modbus RTU 设备", "如：HR:0、C:0、40001",
            "地址首选 C:0、DI:0、HR:0、IR:0；兼容明确功能区手册地址 00001/10001/30001/40001。")
    ];

    public IReadOnlyList<DriverValidationIssue> ValidateChannel(ChannelEntry channel)
        => channel.TransportKind == ChannelTransportKind.Serial
            ? Array.Empty<DriverValidationIssue>()
            : new[] { new DriverValidationIssue("transportKind", "Modbus RTU 当前只接受串口通道") };

    public IReadOnlyList<DriverValidationIssue> ValidateDevice(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel)
    {
        var issues = new List<DriverValidationIssue>();
        if (!string.Equals(device.DriverKey, DriverKey, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("driverKey", "设备驱动键与 Modbus RTU 描述不匹配"));
        if (!DeviceModels.Any(model => string.Equals(model.Key, device.Model, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("model", "请选择通用 Modbus RTU 设备系列"));
        if (device.ModbusUnitId is not >= 1 or > 247)
            issues.Add(new("modbusUnitId", "Modbus RTU 站号必须在 1-247 范围内，首版不支持广播站号 0"));
        if (device.ModbusTcp is not null)
            issues.Add(new("modbusTcp", "Modbus RTU 不能配置 Modbus TCP 端点"));
        if (channel.Serial is null)
            issues.Add(new("channel.serial", "Modbus RTU 必须引用包含串口参数的通道"));
        return issues;
    }

    public IReadOnlyList<DriverValidationIssue> ValidatePoint(
        PointsConfig.PointEntry point,
        DeviceConfig.DeviceEntry device)
        => ModbusPointValidation.Validate(point, device, DriverKey);

    public string NormalizeAddress(PointsConfig.PointEntry point)
        => ModbusPointValidation.NormalizeAddress(point);
}

/// <summary>
/// 两个 Modbus 描述器共用的点位校验实现，防止 TCP/RTU 的类型和地址规则分叉。
/// </summary>
internal static class ModbusPointValidation
{
    public static IReadOnlyList<DriverValidationIssue> Validate(
        PointsConfig.PointEntry point,
        DeviceConfig.DeviceEntry device,
        string driverKey)
    {
        var issues = new List<DriverValidationIssue>();
        if (!ModbusAddressParser.TryParse(point.Address, out var address, out var error))
        {
            issues.Add(new("address", error ?? $"地址“{point.Address}”无法解析"));
            return issues;
        }

        if (point.AddressDefinition is { } definition
            && (!string.IsNullOrWhiteSpace(definition.Area) || definition.Offset.HasValue))
        {
            if (!ModbusAreaExtensions.TryParse(definition.Area, out var definitionArea)
                || definition.Offset != address.Offset
                || definitionArea != address.Area)
                issues.Add(new("addressDefinition", "结构化 Modbus 地址必须与 Address 的区域和零基偏移一致"));
        }

        var dataType = ModbusTypeCapabilities.NormalizeCompatibilityType(address.Area, point.RawDataTypeKind);
        if (!ModbusTypeCapabilities.TryValidate(address.Area, dataType, point.IsWritable, point.DecodeOptions, out var reason))
            issues.Add(new("rawDataType", $"点位“{DisplayPoint(point)}”：{reason}"));
        if (ModbusTypeCapabilities.TryGetRegisterCount(dataType, out var length)
            && address.Offset + length > ushort.MaxValue + 1)
            issues.Add(new("address", "点位占用的寄存器范围不能超过 65535"));

        if (DevicePointTypeCatalog.TryParseProtocol(point.Protocol, out var protocol)
            && ((driverKey == DriverKeyCatalog.ModbusTcp && protocol != DevicePointProtocol.ModbusTcp)
                || (driverKey == DriverKeyCatalog.ModbusRtu && protocol != DevicePointProtocol.ModbusRtu)))
            issues.Add(new("protocol", "点位协议必须与所属 Modbus 驱动一致"));
        return issues;
    }

    public static string NormalizeAddress(PointsConfig.PointEntry point)
        => ModbusAddressParser.TryParse(point.Address, out var address, out var error)
            ? address.Canonical
            : throw new DomainException(error ?? $"Modbus 地址无法解析：{point.Address}");

    private static string DisplayPoint(PointsConfig.PointEntry point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;
}
