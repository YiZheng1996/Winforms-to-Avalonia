using System.Globalization;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备站号的客户可读展示规则。站号属于设备，不属于单个点位。
/// </summary>
internal static class DeviceStationDisplay
{
    public static DeviceConfig.DeviceEntry? ResolveDevice(
        IEnumerable<DeviceConfig.DeviceEntry>? devices,
        string? deviceIdentity)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentity))
            return null;

        var identity = deviceIdentity.Trim();
        var entries = (devices ?? Array.Empty<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();
        return entries.FirstOrDefault(device =>
                   !string.IsNullOrWhiteSpace(device.Id)
                   && string.Equals(device.Id.Trim(), identity, StringComparison.OrdinalIgnoreCase))
               ?? entries.FirstOrDefault(device =>
                   !string.IsNullOrWhiteSpace(device.Code)
                   && string.Equals(device.Code.Trim(), identity, StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatDeviceText(DeviceConfig.DeviceEntry? device)
    {
        if (device is null)
            return "未分配设备";
        if (!string.IsNullOrWhiteSpace(device.Name))
            return device.Name.Trim();
        if (!string.IsNullOrWhiteSpace(device.Code))
            return device.Code.Trim();
        return "未命名设备";
    }

    public static bool IsModbus(string? driverKey)
        => string.Equals(driverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(driverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase);

    public static string FormatValue(DeviceConfig.DeviceEntry? device)
        => device is null ? "—" : FormatValue(device.DriverKey, device.ModbusUnitId);

    public static string FormatValue(string? driverKey, int? unitId)
    {
        if (!IsModbus(driverKey))
            return "不适用";

        return unitId is >= 1 and <= 247
            ? unitId.Value.ToString(CultureInfo.InvariantCulture)
            : "未配置";
    }

    public static string FormatLabel(DeviceConfig.DeviceEntry? device)
        => device is null ? string.Empty : FormatLabel(device.DriverKey, device.ModbusUnitId);

    public static string FormatLabel(string? driverKey, int? unitId)
    {
        if (!IsModbus(driverKey))
            return string.Empty;

        var value = FormatValue(driverKey, unitId);
        return value == "未配置" ? "站号未配置" : $"站号 {value}";
    }
}
