using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 多设备配置的完整交叉校验器。它不创建连接、不访问串口/TCP，也不修改当前生效对象。
/// </summary>
public static class MultiDeviceConfigurationValidator
{
    public static IReadOnlyList<ConfigurationIssue> Validate(
        DeviceConfigurationSnapshot snapshot,
        IEnumerable<IDeviceDriverDescriptor>? descriptors = null,
        IEnumerable<RequiredSignal>? requiredSignals = null)
    {
        var issues = new List<ConfigurationIssue>();
        if (snapshot is null)
        {
            issues.Add(new("snapshot", "不能为空"));
            return issues;
        }
        if (snapshot.SchemaVersion != DeviceConfigurationSnapshot.CurrentSchemaVersion)
            issues.Add(new("snapshot.schemaVersion", $"不受支持（期望 {DeviceConfigurationSnapshot.CurrentSchemaVersion}）"));
        if (string.IsNullOrWhiteSpace(snapshot.Revision))
            issues.Add(new("snapshot.revision", "不能为空"));

        if (snapshot.Device is null || snapshot.Points is null || snapshot.Simulation is null
            || snapshot.SignalBindings is null)
        {
            if (snapshot.Device is null) issues.Add(new("device.json", "配置对象不能为空"));
            if (snapshot.Points is null) issues.Add(new("points.json", "配置对象不能为空"));
            if (snapshot.Simulation is null) issues.Add(new("simulation.json", "配置对象不能为空"));
            if (snapshot.SignalBindings is null) issues.Add(new("signal-bindings.json", "配置对象不能为空"));
            return issues;
        }

        AddException(issues, "device.json", snapshot.Device.Validate);
        AddException(issues, "points.json", snapshot.Points.Validate);
        AddException(issues, "simulation.json", snapshot.Simulation.Validate);

        var driverMap = (descriptors ?? Array.Empty<IDeviceDriverDescriptor>())
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.DriverKey))
            .GroupBy(descriptor => descriptor.DriverKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        ValidateDeviceReferences(snapshot.Device, driverMap, issues);
        ValidatePointGroupReferences(snapshot.Device, snapshot.Points, issues);
        ValidatePointReferences(snapshot.Device, snapshot.Points, driverMap, issues);
        ValidateSimulationReferences(snapshot.Points, snapshot.Simulation, issues);
        issues.AddRange(snapshot.SignalBindings.Validate(
            (snapshot.Points.Points ?? new List<PointsConfig.PointEntry>())
                .Where(point => point is not null)
                .Select(point => point.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        ValidateSignalBindingContracts(snapshot.Points, snapshot.SignalBindings, requiredSignals, issues);

        return issues;
    }

    private static void ValidatePointGroupReferences(
        DeviceConfig deviceConfig,
        PointsConfig pointsConfig,
        ICollection<ConfigurationIssue> issues)
    {
        if (pointsConfig.SchemaVersion < PointsConfig.CurrentSchemaVersion)
            return;

        var devices = (deviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null && !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var groupsByDevice = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in pointsConfig.Groups ?? new List<PointsConfig.PointGroupEntry>())
        {
            if (group is null) continue;
            var path = $"points.json.groups[{group.Code}]";
            if (!devices.ContainsKey(group.DeviceId?.Trim() ?? string.Empty))
                issues.Add(new(path + ".deviceId", $"引用的设备不存在：{group.DeviceId}"));

            var deviceId = group.DeviceId?.Trim() ?? string.Empty;
            if (!groupsByDevice.TryGetValue(deviceId, out var codes))
                groupsByDevice[deviceId] = codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!codes.Add(group.Code?.Trim() ?? string.Empty))
                issues.Add(new(path + ".code", $"同一设备存在重复点位分组编码：{group.Code}"));
        }

        foreach (var device in devices.Values)
        {
            if (!groupsByDevice.TryGetValue(device.Id.Trim(), out var groups) || groups.Count == 0)
                issues.Add(new($"points.json.groups[{device.Code}]", "每个设备至少需要一个点位分组"));
        }
    }

    public static void EnsureValid(
        DeviceConfigurationSnapshot snapshot,
        IEnumerable<IDeviceDriverDescriptor>? descriptors = null,
        IEnumerable<RequiredSignal>? requiredSignals = null)
    {
        var issues = Validate(snapshot, descriptors, requiredSignals);
        if (issues.Count > 0)
            throw new ConfigValidationException(string.Join("；", issues.Select(issue => issue.Message)));
    }

    private static void ValidateDeviceReferences(
        DeviceConfig deviceConfig,
        IReadOnlyDictionary<string, IDeviceDriverDescriptor> drivers,
        ICollection<ConfigurationIssue> issues)
    {
        var channels = deviceConfig.Channels ?? new List<ChannelEntry>();
        var channelMap = channels
            .Where(channel => channel is not null && !string.IsNullOrWhiteSpace(channel.Id))
            .GroupBy(channel => channel.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var device in deviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
        {
            if (device is null)
            {
                issues.Add(new("device.json.devices", "存在空设备项"));
                continue;
            }
            var path = $"device.json.devices[{device.Code}]";
            if (!channelMap.TryGetValue(device.ChannelId?.Trim() ?? string.Empty, out var channel))
                continue;
            if (device.DeviceMode == DeviceMode.Hardware && !channel.Enabled)
                issues.Add(new(path + ".channelId", "硬件设备所属通道已禁用"));

            if (!drivers.TryGetValue(device.DriverKey?.Trim() ?? string.Empty, out var descriptor))
            {
                if (drivers.Count > 0)
                    issues.Add(new(path + ".driverKey", $"设备“{DisplayDevice(device)}”的通信方式未注册"));
                continue;
            }

            foreach (var issue in descriptor.ValidateChannel(channel))
                issues.Add(new(path + ".channel." + issue.Path, issue.Message));
            if (!descriptor.SupportedTransports.Contains(channel.TransportKind))
                issues.Add(new(path + ".channel.transportKind",
                    $"驱动“{descriptor.DisplayName}”不能使用当前通道传输类型：{channel.TransportKind}"));
            foreach (var issue in descriptor.ValidateDevice(device, channel))
                issues.Add(new(path + "." + issue.Path, issue.Message));
            if (device.DeviceMode == DeviceMode.Hardware && !descriptor.IsImplemented)
                issues.Add(new(path + ".driverKey", $"设备“{DisplayDevice(device)}”当前不能在硬件模式使用"));
            if (string.Equals(device.DriverKey, DriverKeyCatalog.Simulation, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(path + ".driverKey", "仿真不能作为设备驱动，请选择实际设备驱动并设置该设备的仿真模式"));
        }
    }

    private static void ValidatePointReferences(
        DeviceConfig deviceConfig,
        PointsConfig pointsConfig,
        IReadOnlyDictionary<string, IDeviceDriverDescriptor> drivers,
        ICollection<ConfigurationIssue> issues)
    {
        var devices = (deviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null && !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var duplicateAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var point in pointsConfig.Points ?? new List<PointsConfig.PointEntry>())
        {
            if (point is null)
            {
                issues.Add(new("points.json.points", "存在空点位项"));
                continue;
            }
            var path = $"points.json.points[{point.Code}]";
            if (!devices.TryGetValue(point.DeviceId?.Trim() ?? string.Empty, out var device))
            {
                issues.Add(new(path + ".deviceId", $"引用的设备不存在：{point.DeviceId}"));
                continue;
            }

            if (!drivers.TryGetValue(device.DriverKey?.Trim() ?? string.Empty, out var descriptor))
            {
                if (drivers.Count > 0)
                    issues.Add(new(path + ".deviceId", $"设备“{DisplayDevice(device)}”的通信方式未注册"));
                var fallbackAddress = point.AddressDefinition?.ToCanonical(point.Address) ?? point.Address.Trim();
                if (!duplicateAddresses.Add($"{device.Id.Trim()}\u001f{fallbackAddress}"))
                    issues.Add(new(path + ".address", $"同一设备存在重复规范地址：{fallbackAddress}"));
                continue;
            }

            string normalized;
            try
            {
                normalized = descriptor.NormalizeAddress(point);
            }
            catch (Exception ex)
            {
                issues.Add(new(path + ".address", ex.Message));
                continue;
            }

            var addressKey = $"{device.Id.Trim()}\u001f{normalized}";
            if (!duplicateAddresses.Add(addressKey))
                issues.Add(new(path + ".address", $"同一设备存在重复规范地址：{normalized}"));

            foreach (var issue in descriptor.ValidatePoint(point, device))
                issues.Add(new(path + "." + issue.Path, issue.Message));
            if (point.IsWritable && point.WritePolicy != PointWritePolicy.ReadBackEqual)
                issues.Add(new(path + ".writePolicy", "第一版只支持 ReadBackEqual"));
        }
    }

    private static void ValidateSimulationReferences(
        PointsConfig pointsConfig,
        SimulationConfig simulation,
        ICollection<ConfigurationIssue> issues)
    {
        var pointIds = (pointsConfig.Points ?? new List<PointsConfig.PointEntry>())
            .Where(point => point is not null)
            .Select(point => point.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if ((simulation.InitialValues ?? new Dictionary<string, object?>()).Count > 0)
            issues.Add(new("simulation.json.initialValues", "v2 必须使用 PointId，不能继续使用地址作为缓存主键"));
        foreach (var pointId in (simulation.InitialValuesByPointId ?? new Dictionary<string, object?>()).Keys)
            if (!pointIds.Contains(pointId))
                issues.Add(new("simulation.json.initialValuesByPointId", $"引用的 PointId 不存在：{pointId}"));
        foreach (var rule in simulation.ChangeRules ?? new List<SimulationConfig.ChangeRule>())
            if (!pointIds.Contains(rule.PointId))
                issues.Add(new("simulation.json.changeRules", $"引用的 PointId 不存在：{rule.PointId}"));
        foreach (var scenario in simulation.FaultInjectionScenarios ?? new List<SimulationConfig.FaultInjection>())
            if (!pointIds.Contains(scenario.PointId))
                issues.Add(new("simulation.json.faultInjectionScenarios", $"引用的 PointId 不存在：{scenario.PointId}"));
    }

    private static void ValidateSignalBindingContracts(
        PointsConfig pointsConfig,
        SignalBindingsConfig bindingsConfig,
        IEnumerable<RequiredSignal>? requiredSignals,
        ICollection<ConfigurationIssue> issues)
    {
        if (requiredSignals is null) return;
        var requirements = requiredSignals
            .Where(requirement => requirement is not null && !string.IsNullOrWhiteSpace(requirement.SignalKey))
            .GroupBy(requirement => requirement.SignalKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var points = (pointsConfig.Points ?? new List<PointsConfig.PointEntry>())
            .Where(point => point is not null && !string.IsNullOrWhiteSpace(point.Id))
            .GroupBy(point => point.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var binding in bindingsConfig.Bindings ?? new Dictionary<string, string>())
        {
            var signalKey = binding.Key?.Trim() ?? string.Empty;
            var pointId = binding.Value?.Trim() ?? string.Empty;
            if (!requirements.TryGetValue(signalKey, out var requirement))
            {
                issues.Add(new($"signal-bindings.{binding.Key}", "绑定键未被任何已注册执行器声明"));
                continue;
            }
            if (!points.TryGetValue(pointId, out var point)) continue;
            if (!IsTypeCompatible(requirement.ExpectedDataType, point.RawDataTypeKind))
                issues.Add(new($"signal-bindings.{binding.Key}", $"类型不匹配：期望 {DevicePointTypeCatalog.ToDisplayName(requirement.ExpectedDataType)}，实际 {DevicePointTypeCatalog.ToDisplayName(point.RawDataTypeKind)}"));
            if (requirement.MaxSampleAge <= TimeSpan.Zero)
                issues.Add(new($"signal-bindings.{binding.Key}", "最大样本年龄必须大于 0"));
        }
    }

    private static bool IsTypeCompatible(DevicePointDataType expected, DevicePointDataType actual)
        => expected == actual
           || (expected is DevicePointDataType.Boolean or DevicePointDataType.Bool
               && actual is DevicePointDataType.Boolean or DevicePointDataType.Bool);

    private static void AddException(
        ICollection<ConfigurationIssue> issues,
        string path,
        Action validator)
    {
        try { validator(); }
        catch (ConfigValidationException ex) { issues.Add(new(path, ex.Message)); }
    }

    private static string DisplayDevice(DeviceConfig.DeviceEntry device)
        => string.IsNullOrWhiteSpace(device.Name) ? device.Code : device.Name;
}
