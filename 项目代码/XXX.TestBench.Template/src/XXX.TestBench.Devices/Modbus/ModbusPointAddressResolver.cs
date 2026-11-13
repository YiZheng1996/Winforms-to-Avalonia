using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// 根据已配置设备上下文解析 Modbus 点位地址；信捷地址不能仅凭文本前缀判定。
/// </summary>
public static class ModbusPointAddressResolver
{
    public static bool IsXinjeFieldV1(string? model)
        => string.Equals(
            model?.Trim(),
            DeviceModelKeyCatalog.XinjeFieldV1,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedXinjeDataType(
        XinjeFieldV1Area area,
        DevicePointDataType type)
        => area is XinjeFieldV1Area.M or XinjeFieldV1Area.HM
            ? type == DevicePointDataType.Bool
            : type is DevicePointDataType.UInt16
                or DevicePointDataType.Float32
                or DevicePointDataType.Int32
                or DevicePointDataType.UInt32;

    public static bool TryResolve(
        DeviceConfig.DeviceEntry device,
        PointsConfig.PointEntry point,
        out ModbusAddress address,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(point);
        return TryResolve(device.DriverKey, device.Model, point.Address, point.AddressDefinition, out address, out error);
    }

    public static bool TryResolve(
        DeviceConfig.DeviceEntry device,
        DevicePoint point,
        out ModbusAddress address,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(point);
        return TryResolve(device.DriverKey, device.Model, point.Address, point.AddressDefinition, out address, out error);
    }

    public static ModbusAddress Resolve(DeviceConfig.DeviceEntry device, DevicePoint point)
        => TryResolve(device, point, out var address, out var error)
            ? address
            : throw new DomainException(error ?? $"Modbus 地址无法解析：{point.Address}");

    public static bool TryResolve(
        string? driverKey,
        string? model,
        string? addressText,
        PointAddressDefinition? definition,
        out ModbusAddress address,
        out string? error)
    {
        if (IsXinjeFieldV1(model))
        {
            if (!string.Equals(driverKey?.Trim(), DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase))
            {
                address = default;
                error = "信捷现场验证配置 V1 仅支持 Modbus TCP，不支持 Modbus RTU";
                return false;
            }

            if (!XinjeFieldV1AddressParser.TryParse(addressText, out var xinjeAddress, out error))
            {
                address = default;
                return false;
            }
            address = xinjeAddress.ModbusAddress;
        }
        else
        {
            if (!ModbusAddressParser.TryParse(addressText, out address, out error))
                return false;
        }

        if (definition is not null
            && (!string.IsNullOrWhiteSpace(definition.Area) || definition.Offset.HasValue))
        {
            if (!ModbusAreaExtensions.TryParse(definition.Area, out var definitionArea)
                || definition.Offset != address.Offset
                || definitionArea != address.Area)
            {
                error = "结构化 Modbus 地址必须与 Address 的区域和零基偏移一致";
                address = default;
                return false;
            }
        }

        error = null;
        return true;
    }
}
