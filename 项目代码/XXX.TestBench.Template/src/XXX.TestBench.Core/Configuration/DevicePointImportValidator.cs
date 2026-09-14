using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 导入预览阶段的设备级点位预校验。
///
/// 它不连接设备，只把导入行放到已配置设备对应的驱动描述器中校验，
/// 这样地址格式错误会在确认保存前暴露；最终完整配置应用仍会再次校验。
/// </summary>
public static class DevicePointImportValidator
{
    public static IReadOnlyList<ConfigurationIssue> Validate(
        DeviceConfig deviceConfig,
        IEnumerable<PointsConfig.PointEntry> points,
        IEnumerable<IDeviceDriverDescriptor>? descriptors)
    {
        ArgumentNullException.ThrowIfNull(deviceConfig);
        ArgumentNullException.ThrowIfNull(points);

        var issues = new List<ConfigurationIssue>();
        var devices = (deviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();
        var drivers = (descriptors ?? Array.Empty<IDeviceDriverDescriptor>())
            .Where(descriptor => descriptor is not null && !string.IsNullOrWhiteSpace(descriptor.DriverKey))
            .GroupBy(descriptor => descriptor.DriverKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var point in points.Where(point => point is not null))
        {
            var pointLabel = string.IsNullOrWhiteSpace(point.Code) ? point.Name : point.Code;
            var path = $"导入点位[{pointLabel}]";
            var device = FindDevice(point, devices);
            if (device is null)
            {
                issues.Add(new(path, string.IsNullOrWhiteSpace(point.DeviceCode)
                    ? "未能从当前模板范围确定所属设备"
                    : $"引用的设备不存在：{point.DeviceCode}"));
                continue;
            }

            if (!drivers.TryGetValue(device.DriverKey?.Trim() ?? string.Empty, out var descriptor))
            {
                issues.Add(new(path,
                    $"设备“{DisplayDevice(device)}”的通信方式未注册，无法校验地址"));
                continue;
            }

            string normalized;
            try
            {
                normalized = descriptor.NormalizeAddress(point);
            }
            catch (Exception ex)
            {
                issues.Add(new(path + ".地址", ex.Message));
                continue;
            }

            var addressKey = device.Id.Trim() + "\u001f" + normalized;
            if (!addresses.TryAdd(addressKey, pointLabel))
                issues.Add(new(path + ".地址", $"同一设备的地址重复：{point.Address}"));

            foreach (var issue in descriptor.ValidatePoint(point, device))
                issues.Add(new(path + "." + issue.Path, issue.Message));
        }

        return issues;
    }

    private static DeviceConfig.DeviceEntry? FindDevice(
        PointsConfig.PointEntry point,
        IReadOnlyList<DeviceConfig.DeviceEntry> devices)
    {
        if (!string.IsNullOrWhiteSpace(point.DeviceId))
        {
            var byId = devices.FirstOrDefault(device =>
                string.Equals(device.Id, point.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(point.DeviceCode))
        {
            var byCode = devices.FirstOrDefault(device =>
                string.Equals(device.Code, point.DeviceCode, StringComparison.OrdinalIgnoreCase));
            if (byCode is not null) return byCode;
        }

        return string.IsNullOrWhiteSpace(point.DeviceId)
            && string.IsNullOrWhiteSpace(point.DeviceCode)
            && devices.Count == 1
            ? devices[0]
            : null;
    }

    private static string DisplayDevice(DeviceConfig.DeviceEntry device)
        => string.IsNullOrWhiteSpace(device.Name) ? device.Code : device.Name;
}
