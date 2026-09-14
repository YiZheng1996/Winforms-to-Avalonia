using S7.Net;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Siemens;

namespace XXX.TestBench.Devices.Drivers;

/// <summary>
/// 西门子 S7 地址和数据类型描述器。解析规则与运行时共用 S7AddressParser。
/// </summary>
public sealed class SiemensS7DriverDescriptor : IDeviceDriverDescriptor
{
    public string DriverKey => DriverKeyCatalog.SiemensS7;
    public string DisplayName => "西门子 S7";
    public bool IsImplemented => true;
    public IReadOnlySet<ChannelTransportKind> SupportedTransports { get; } =
        new HashSet<ChannelTransportKind> { ChannelTransportKind.Tcp };

    public IReadOnlyList<DeviceModelDescriptor> DeviceModels { get; } =
    [
        new("S7-200 SMART", "S7-200 SMART", "如：VW5022", "S7-200 SMART：VW5022、VD800、V0.1、M10.1。V 区运行时映射为 DB2。"),
        new("S7-1200", "S7-1200", "如：DB144.DBD88", "S7-1200：DB144.DBD88、DB142.DBX22.3、M10.1。"),
        new("S7-1500", "S7-1500", "如：DB144.DBD88", "S7-1500：DB144.DBD88、DB142.DBX22.3、M10.1。")
    ];

    public IReadOnlySet<DevicePointDataType> SupportedDataTypes => SiemensS7TypeCapabilities.SupportedTypes;

    public IReadOnlyList<DriverValidationIssue> ValidateChannel(ChannelEntry channel)
        => channel.TransportKind == ChannelTransportKind.Tcp
            ? Array.Empty<DriverValidationIssue>()
            : new[] { new DriverValidationIssue("transportKind", "西门子 S7 当前只接受 TCP 通道配置") };

    public IReadOnlyList<DriverValidationIssue> ValidateDevice(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel)
    {
        var issues = new List<DriverValidationIssue>();
        if (!string.Equals(device.DriverKey, DriverKey, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("driverKey", "设备驱动键与西门子 S7 描述不匹配"));
        if (!DeviceModels.Any(model => string.Equals(model.Key, device.Model, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("model", "请选择受支持的西门子系列：S7-200 SMART、S7-1200 或 S7-1500"));
        if (device.DeviceMode == DeviceMode.Hardware
            && device.SiemensS7 is null
            && string.IsNullOrWhiteSpace(device.Address)
            && (channel.Tcp is null || string.IsNullOrWhiteSpace(channel.Tcp.Host)))
            issues.Add(new("siemensS7", "硬件 Siemens S7 必须配置设备专属端点"));
        return issues;
    }

    public IReadOnlyList<DriverValidationIssue> ValidatePoint(
        PointsConfig.PointEntry point,
        DeviceConfig.DeviceEntry device)
    {
        var issues = new List<DriverValidationIssue>();
        if (!S7AddressParser.TryParse(point.Address, out var address, out var error))
        {
            issues.Add(new("address", error ?? $"地址“{point.Address}”无法解析"));
            return issues;
        }

        var is200 = device.Model.Contains("200", StringComparison.OrdinalIgnoreCase)
            && !device.Model.Contains("1200", StringComparison.OrdinalIgnoreCase);
        var is1200Or1500 = device.Model.Contains("1200", StringComparison.OrdinalIgnoreCase)
            || device.Model.Contains("1500", StringComparison.OrdinalIgnoreCase);
        if (is200 && !address!.Canonical.StartsWith("V", StringComparison.OrdinalIgnoreCase)
            && address.Area == DataType.DataBlock)
            issues.Add(new("address", "地址无法导入该设备：S7-200 SMART 的数据块地址应使用 V/VB/VW/VD 形式"));
        if (is1200Or1500 && address!.Canonical.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("address", $"地址无法导入该设备：{device.Model} 不支持 S7-200 SMART 的 V 区地址"));

        var dataType = point.RawDataTypeKind;
        if (device.DeviceMode == DeviceMode.Simulation && dataType == DevicePointDataType.Decimal)
            return issues;
        if (!SiemensS7TypeCapabilities.IsShapeCompatible(address!.Shape, dataType, out var reason))
            issues.Add(new("rawDataType", $"点位“{DisplayPoint(point)}”：{reason}"));
        return issues;
    }

    public string NormalizeAddress(PointsConfig.PointEntry point)
        => S7AddressParser.TryParse(point.Address, out var address, out _)
            ? address!.Canonical
            : (point.Address ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("　", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string DisplayPoint(PointsConfig.PointEntry point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;
}
