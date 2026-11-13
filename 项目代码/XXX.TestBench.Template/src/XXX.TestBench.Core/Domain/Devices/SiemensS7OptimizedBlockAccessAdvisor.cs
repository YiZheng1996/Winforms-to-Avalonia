using System.Text.RegularExpressions;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 客户可见的西门子 S7 优化块访问提示。文案集中在此处，页面只负责展示和确认。
/// </summary>
public sealed record SiemensS7OptimizedBlockAccessNotice(
    IReadOnlyList<string> Addresses,
    string ShortMessage,
    string DetailMessage,
    string ConfirmationTitle,
    string ConfirmationMessage,
    string ConfirmButtonText);

/// <summary>
/// 判定西门子 S7 的 DB 绝对地址，并生成统一的客户提示。
/// </summary>
public static class SiemensS7OptimizedBlockAccessAdvisor
{
    private const string ConfirmationTitleText = "请确认数据块访问设置";
    private const string DetailText =
        "请在 TIA Portal 中按以下步骤处理：\n" +
        "1. 打开对应数据块的属性。\n" +
        "2. 取消勾选“优化的块访问”。\n" +
        "3. 重新下载 PLC。\n" +
        "4. 重新连接或导入后再使用。";

    private static readonly Regex DbAbsoluteAddress = new(
        @"^%?DB\d+\.(?:DBX\d+\.[0-7]|DBB\d+|DBW\d+|DBD\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 只匹配 DB1.DBX0.0、DB1.DBB0、DB1.DBW0、DB1.DBD0 这类 DB 绝对地址。
    /// </summary>
    public static bool IsDbAbsoluteAddress(string? address)
        => !string.IsNullOrWhiteSpace(address) && DbAbsoluteAddress.IsMatch(address.Trim());

    /// <summary>
    /// 判断是否为当前项目使用的西门子 S7 驱动。
    /// </summary>
    public static bool IsSiemensS7Driver(string? driverKey)
        => string.Equals(driverKey?.Trim(), DriverKeyCatalog.SiemensS7, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 硬件模式下使用 S7 DB 绝对地址时，保存或应用前需要确认。
    /// </summary>
    public static bool ShouldRequireConfirmation(
        string? driverKey,
        DeviceMode deviceMode,
        string? address)
        => deviceMode == DeviceMode.Hardware
            && IsSiemensS7Driver(driverKey)
            && IsDbAbsoluteAddress(address);

    /// <summary>
    /// 编辑器地址区使用的短提示；适用于西门子 S7，不展示内部字段名。
    /// </summary>
    public static SiemensS7OptimizedBlockAccessNotice? CreateDisplayNotice(
        string? driverKey,
        string? address)
    {
        if (!IsSiemensS7Driver(driverKey) || !IsDbAbsoluteAddress(address))
            return null;

        return CreateNotice(new[] { address!.Trim() }, requireHardwareMode: false);
    }

    /// <summary>
    /// 从设备配置和点位集合中提取需要提示的 S7 DB 绝对地址。
    /// previousPoints 为空时检查全部点位；提供 previousPoints 时只检查新增或地址/设备发生变化的风险。
    /// </summary>
    public static SiemensS7OptimizedBlockAccessNotice? Inspect(
        IEnumerable<PointsConfig.PointEntry>? points,
        IEnumerable<DeviceConfig.DeviceEntry>? devices,
        bool requireHardwareMode = false,
        IEnumerable<PointsConfig.PointEntry>? previousPoints = null,
        IEnumerable<DeviceConfig.DeviceEntry>? previousDevices = null)
    {
        var currentList = (points ?? Array.Empty<PointsConfig.PointEntry>())
            .Where(point => point is not null)
            .ToList();
        var deviceList = (devices ?? Array.Empty<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();
        var previousPointList = previousPoints?.Where(point => point is not null).ToList();
        var previousDeviceList = (previousDevices ?? Array.Empty<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();

        var addresses = new List<string>();
        foreach (var point in currentList)
        {
            if (!IsDbAbsoluteAddress(point.Address))
                continue;

            var device = FindDevice(point, deviceList);
            var isSiemensS7 = IsSiemensS7Driver(device?.DriverKey)
                || (device is null && point.ProtocolKind == DevicePointProtocol.SiemensS7);
            if (!isSiemensS7)
                continue;
            if (requireHardwareMode && device?.DeviceMode != DeviceMode.Hardware)
                continue;

            if (previousPointList is not null)
            {
                var previous = FindPoint(point, previousPointList);
                var previousDevice = previous is null ? null : FindDevice(previous, previousDeviceList);
                if (previous is not null && IsSameRisk(point, device, previous, previousDevice, requireHardwareMode))
                    continue;
            }

            addresses.Add(point.Address.Trim());
        }

        return addresses.Count == 0
            ? null
            : CreateNotice(addresses, requireHardwareMode);
    }

    /// <summary>
    /// 连接或首次读取失败时使用的中文排查提示；非 DB 绝对地址返回 null。
    /// </summary>
    public static string? GetFailureHint(string? address)
    {
        if (!IsDbAbsoluteAddress(address))
            return null;

        return $"该点位使用数据块绝对地址“{address!.Trim()}”。如果对应数据块启用了“优化的块访问”，程序可能连接成功但读不到数据。请在 TIA Portal 中打开对应数据块属性，取消“优化的块访问”，重新下载 PLC 后再连接。";
    }

    private static SiemensS7OptimizedBlockAccessNotice CreateNotice(
        IReadOnlyList<string> sourceAddresses,
        bool requireHardwareMode)
    {
        var addresses = sourceAddresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shown = string.Join("、", addresses.Take(3));
        var shortMessage = addresses.Count == 1
            ? $"检测到 PLC 绝对地址：{shown}。请确认 PLC 中对应数据块已关闭“优化的块访问”，否则可能连接成功但读不到数据。"
            : $"检测到 {addresses.Count} 个 PLC 绝对地址：{shown}。请确认 PLC 中对应数据块已关闭“优化的块访问”，否则导入后可能读不到数据。";
        var confirmationMessage =
            $"您填写或导入的点位包含 PLC 绝对地址：{shown}。\n\n" +
            "如果对应数据块在 TIA Portal 中启用了“优化的块访问”，程序可能连接成功但读不到该绝对地址。\n\n" +
            "请先打开对应数据块属性，取消“优化的块访问”，重新下载 PLC，再继续保存。\n\n" +
            "是否继续？";

        return new SiemensS7OptimizedBlockAccessNotice(
            addresses,
            shortMessage,
            DetailText,
            ConfirmationTitleText,
            confirmationMessage,
            "继续保存");
    }

    private static DeviceConfig.DeviceEntry? FindDevice(
        PointsConfig.PointEntry point,
        IReadOnlyList<DeviceConfig.DeviceEntry> devices)
    {
        if (!string.IsNullOrWhiteSpace(point.DeviceId))
        {
            var byId = devices.FirstOrDefault(device =>
                string.Equals(device.Id?.Trim(), point.DeviceId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(point.DeviceCode))
        {
            var byCode = devices.FirstOrDefault(device =>
                string.Equals(device.Code?.Trim(), point.DeviceCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byCode is not null) return byCode;
        }

        return string.IsNullOrWhiteSpace(point.DeviceId)
            && string.IsNullOrWhiteSpace(point.DeviceCode)
            && devices.Count == 1
                ? devices[0]
                : null;
    }

    private static PointsConfig.PointEntry? FindPoint(
        PointsConfig.PointEntry point,
        IReadOnlyList<PointsConfig.PointEntry> previousPoints)
    {
        if (!string.IsNullOrWhiteSpace(point.Id))
        {
            var byId = previousPoints.FirstOrDefault(previous =>
                string.Equals(previous.Id?.Trim(), point.Id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(point.Code))
        {
            return previousPoints.FirstOrDefault(previous =>
                string.Equals(previous.Code?.Trim(), point.Code.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(previous.DeviceId?.Trim(), point.DeviceId?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(previous.DeviceCode?.Trim(), point.DeviceCode?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static bool IsSameRisk(
        PointsConfig.PointEntry current,
        DeviceConfig.DeviceEntry? currentDevice,
        PointsConfig.PointEntry previous,
        DeviceConfig.DeviceEntry? previousDevice,
        bool requireHardwareMode)
    {
        if (!string.Equals(current.Address?.Trim(), previous.Address?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var currentDriver = currentDevice?.DriverKey?.Trim() ?? string.Empty;
        var previousDriver = previousDevice?.DriverKey?.Trim() ?? string.Empty;
        if (!string.Equals(currentDriver, previousDriver, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(currentDevice?.Id?.Trim(), previousDevice?.Id?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        return !requireHardwareMode
            || (currentDevice?.DeviceMode == DeviceMode.Hardware
                && previousDevice?.DeviceMode == DeviceMode.Hardware);
    }
}
