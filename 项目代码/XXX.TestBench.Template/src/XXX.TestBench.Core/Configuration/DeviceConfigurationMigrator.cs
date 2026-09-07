using System.Security.Cryptography;
using System.Text;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// v1 四文件配置到当前完整配置快照的显式迁移入口。
/// 迁移只在内存中构造候选和报告，不覆盖旧文件，也不自动应用候选。
/// </summary>
public static class DeviceConfigurationMigrator
{
    public static ConfigurationMigrationResult Migrate(
        DeviceConfig legacyDevice,
        PointsConfig legacyPoints,
        SimulationConfig legacySimulation,
        string? revision = null)
    {
        var issues = new List<ConfigurationIssue>();
        var oldDevices = legacyDevice.Devices ?? new List<DeviceConfig.DeviceEntry>();
        var oldPoints = legacyPoints.Points ?? new List<PointsConfig.PointEntry>();

        var channels = new List<ChannelEntry>();
        var devices = new List<DeviceConfig.DeviceEntry>();
        var deviceByLegacyIndex = new List<(DeviceConfig.DeviceEntry Source, DeviceConfig.DeviceEntry Target)>();

        for (var index = 0; index < oldDevices.Count; index++)
        {
            var source = oldDevices[index];
            var deviceId = StableId($"device|{source.Name.Trim()}|{source.Protocol.Trim()}|{source.Address.Trim()}");
            var channelId = StableId($"channel|{source.Protocol.Trim()}|{source.Address.Trim()}");
            var isSimulation = DevicePointTypeCatalog.TryParseProtocol(source.Protocol, out var protocol)
                && protocol == DevicePointProtocol.Simulation;
            var driverKey = isSimulation
                ? DriverKeyCatalog.Simulation
                : NormalizeLegacyDriverKey(source.Protocol);
            var poll = source.PollIntervalMs > 0 ? source.PollIntervalMs : legacyDevice.PollIntervalMs;
            var timeout = legacyDevice.TimeoutMs > 0 ? legacyDevice.TimeoutMs : 1000;
            var staleAfter = (int)Math.Min(int.MaxValue,
                Math.Max(3L * Math.Max(1, poll), 2L * Math.Max(1, timeout)));

            var channel = new ChannelEntry
            {
                Id = channelId,
                Code = "CH_" + ShortId(channelId),
                Name = string.IsNullOrWhiteSpace(source.Name) ? "迁移通道" : source.Name.Trim() + "通道",
                TransportKind = isSimulation ? ChannelTransportKind.Simulation : ChannelTransportKind.Unknown,
                Enabled = source.Enabled,
                TimeoutMs = timeout,
                RetryCount = 0,
                Simulation = isSimulation ? new SimulationChannelParameters
                {
                    InstanceKey = string.IsNullOrWhiteSpace(source.Address) ? ShortId(deviceId) : source.Address.Trim()
                } : null
            };
            channels.Add(channel);

            var target = new DeviceConfig.DeviceEntry
            {
                Id = deviceId,
                Code = "DEV_" + ShortId(deviceId),
                Name = string.IsNullOrWhiteSpace(source.Name) ? "迁移设备" : source.Name.Trim(),
                Protocol = source.Protocol.Trim(),
                Address = source.Address.Trim(),
                ChannelId = channelId,
                DriverKey = driverKey,
                Manufacturer = string.Empty,
                Model = string.Empty,
                CpuProfile = string.Empty,
                PollIntervalMs = Math.Max(1, poll),
                StaleAfterMs = staleAfter,
                Enabled = source.Enabled
            };
            devices.Add(target);
            deviceByLegacyIndex.Add((source, target));

            if (!isSimulation)
                issues.Add(new($"device.json.devices[{index}].protocol",
                    $"旧协议“{source.Protocol}”未映射为已实现驱动，请在候选配置中补充 DriverKey/通道参数"));
        }

        if (oldDevices.Count == 0)
            issues.Add(new("device.json.devices", "旧配置没有设备，无法自动确定点位归属"));

        var pointEntries = new List<PointsConfig.PointEntry>();
        var pointByAddress = new Dictionary<string, List<PointsConfig.PointEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in oldPoints)
        {
            var targetDevice = ResolveDevice(source, deviceByLegacyIndex, issues);
            var point = new PointsConfig.PointEntry
            {
                Id = StableId($"point|{source.Code.Trim()}|{source.Protocol.Trim()}|{source.Address.Trim()}"),
                Code = source.Code.Trim(),
                Name = string.IsNullOrWhiteSpace(source.Name) ? source.Code.Trim() : source.Name.Trim(),
                Protocol = source.Protocol.Trim(),
                Address = source.Address.Trim(),
                DeviceId = targetDevice?.Id ?? string.Empty,
                DataType = source.DataType.Trim(),
                RawDataType = source.DataType.Trim(),
                Unit = source.Unit?.Trim() ?? string.Empty,
                IsWritable = source.IsWritable,
                IsEnabled = source.IsEnabled,
                RiskLevel = source.RiskLevel,
                Scale = CloneScale(source.Scale),
                RawMin = source.RawMin,
                RawMax = source.RawMax,
                EngMin = source.EngMin,
                EngMax = source.EngMax,
                Description = source.Description?.Trim() ?? string.Empty,
                AddressDefinition = new PointAddressDefinition { LogicalAddress = source.Address.Trim() },
                DecodeOptions = new DecodeOptions(),
                WritePolicy = PointWritePolicy.ReadBackEqual
            };
            if (targetDevice is not null)
                point.GroupId = StableId($"group|{targetDevice.Id}|DEFAULT");
            pointEntries.Add(point);
            var addressKey = source.Address.Trim();
            if (!pointByAddress.TryGetValue(addressKey, out var sameAddress))
                pointByAddress[addressKey] = sameAddress = new List<PointsConfig.PointEntry>();
            sameAddress.Add(point);
        }

        var simulation = new SimulationConfig
        {
            SchemaVersion = SimulationConfig.CurrentSchemaVersion
        };
        foreach (var pair in legacySimulation.InitialValues)
        {
            var matches = FindSimulationPoints(pair.Key, pointByAddress);
            if (matches.Count != 1)
            {
                issues.Add(new("simulation.json.initialValues." + pair.Key,
                    matches.Count == 0 ? "找不到唯一的仿真点位" : "匹配到多个仿真点位，禁止猜测"));
                continue;
            }
            simulation.InitialValuesByPointId[matches[0].Id] = pair.Value;
        }
        foreach (var source in legacySimulation.ChangeRules)
        {
            var matches = FindSimulationPoints(source.Address, pointByAddress);
            if (matches.Count != 1)
            {
                issues.Add(new("simulation.json.changeRules." + source.Address,
                    matches.Count == 0 ? "找不到唯一的仿真点位" : "匹配到多个仿真点位，禁止猜测"));
                continue;
            }
            simulation.ChangeRules.Add(new SimulationConfig.ChangeRule
            {
                PointId = matches[0].Id,
                Address = source.Address.Trim(),
                Pattern = source.Pattern.Trim(),
                RatePerSecond = source.RatePerSecond,
                Min = source.Min,
                Max = source.Max
            });
        }
        foreach (var source in legacySimulation.FaultInjectionScenarios)
        {
            var matches = FindSimulationPoints(source.Address, pointByAddress);
            if (matches.Count != 1)
            {
                issues.Add(new("simulation.json.faultInjectionScenarios." + source.Name,
                    matches.Count == 0 ? "找不到唯一的仿真点位" : "匹配到多个仿真点位，禁止猜测"));
                continue;
            }
            simulation.FaultInjectionScenarios.Add(new SimulationConfig.FaultInjection
            {
                PointId = matches[0].Id,
                Name = source.Name.Trim(),
                Address = source.Address.Trim(),
                Behavior = source.Behavior.Trim()
            });
        }

        var candidate = new DeviceConfigurationSnapshot
        {
            Revision = revision ?? "migration-" + ShortId(StableId(BuildRevisionSeed(oldDevices, oldPoints, legacySimulation))),
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                DeviceMode = legacyDevice.DeviceMode,
                PollIntervalMs = legacyDevice.PollIntervalMs,
                TimeoutMs = legacyDevice.TimeoutMs,
                Channels = channels,
                Devices = devices
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = devices.Select(device => new PointsConfig.PointGroupEntry
                {
                    Id = StableId($"group|{device.Id}|DEFAULT"),
                    DeviceId = device.Id,
                    Code = "DEFAULT",
                    Name = "未分组",
                    Description = "由旧版点位配置迁移生成的默认分组",
                    SortOrder = 0
                }).ToList(),
                Points = pointEntries
            },
            Simulation = simulation,
            SignalBindings = new SignalBindingsConfig()
        };

        issues.AddRange(MultiDeviceConfigurationValidator.Validate(candidate));
        return new ConfigurationMigrationResult(candidate, issues.Distinct().ToList());
    }

    /// <summary>
    /// 将已存在的 v2 完整快照迁移为带单层点位分组的 v3 快照。
    /// 迁移不改变设备、点位、地址或仿真引用的身份，只为每个设备生成稳定的 DEFAULT 分组，
    /// 并把所有未分组点位归入该分组。
    /// </summary>
    public static ConfigurationMigrationResult MigrateToV3(
        DeviceConfigurationSnapshot source,
        string? revision = null)
    {
        if (source.Points.SchemaVersion == PointsConfig.CurrentSchemaVersion)
            return new ConfigurationMigrationResult(source, Array.Empty<ConfigurationIssue>());

        var devices = source.Device.Devices ?? new List<DeviceConfig.DeviceEntry>();
        var groups = devices.Select(device => new PointsConfig.PointGroupEntry
        {
            Id = StableId($"group|{device.Id}|DEFAULT"),
            DeviceId = device.Id,
            Code = "DEFAULT",
            Name = "未分组",
            Description = "由旧版点位配置迁移生成的默认分组",
            SortOrder = 0
        }).ToList();
        var defaultGroupByDevice = groups
            .ToDictionary(group => group.DeviceId.Trim(), group => group.Id, StringComparer.OrdinalIgnoreCase);

        var points = (source.Points.Points ?? new List<PointsConfig.PointEntry>())
            .Where(point => point is not null)
            .Select(point => ClonePoint(point, defaultGroupByDevice))
            .ToList();
        var candidate = new DeviceConfigurationSnapshot
        {
            SchemaVersion = source.SchemaVersion,
            Revision = revision ?? "migration-v3-" + ShortId(StableId(
                source.Revision + "|" + string.Join("|", devices.Select(device => device.Id))
                + "|" + string.Join("|", points.Select(point => point.Id)))),
            Device = source.Device,
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = groups,
                Points = points
            },
            Simulation = source.Simulation,
            SignalBindings = source.SignalBindings
        };

        var issues = MultiDeviceConfigurationValidator.Validate(candidate);
        return new ConfigurationMigrationResult(candidate, issues.Distinct().ToList());
    }

    private static PointsConfig.PointEntry ClonePoint(
        PointsConfig.PointEntry source,
        IReadOnlyDictionary<string, string> defaultGroupByDevice)
        => new()
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Protocol = source.Protocol,
            DeviceId = source.DeviceId,
            DeviceCode = source.DeviceCode,
            GroupId = string.IsNullOrWhiteSpace(source.GroupId)
                && defaultGroupByDevice.TryGetValue(source.DeviceId.Trim(), out var defaultGroupId)
                ? defaultGroupId
                : source.GroupId,
            GroupCode = source.GroupCode,
            Address = source.Address,
            AddressDefinition = source.AddressDefinition,
            DataType = source.DataType,
            RawDataType = source.RawDataType,
            DecodeOptions = source.DecodeOptions,
            WritePolicy = source.WritePolicy,
            Unit = source.Unit,
            IsWritable = source.IsWritable,
            IsEnabled = source.IsEnabled,
            RiskLevel = source.RiskLevel,
            Scale = CloneScale(source.Scale),
            RawMin = source.RawMin,
            RawMax = source.RawMax,
            EngMin = source.EngMin,
            EngMax = source.EngMax,
            Description = source.Description
        };

    private static DeviceConfig.DeviceEntry? ResolveDevice(
        PointsConfig.PointEntry point,
        IReadOnlyList<(DeviceConfig.DeviceEntry Source, DeviceConfig.DeviceEntry Target)> devices,
        ICollection<ConfigurationIssue> issues)
    {
        if (devices.Count == 1) return devices[0].Target;
        var matches = devices
            .Where(item => string.Equals(item.Source.Protocol.Trim(), point.Protocol.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Target)
            .ToList();
        if (matches.Count == 1) return matches[0];
        issues.Add(new($"points.json.points[{point.Code}].deviceId",
            matches.Count == 0 ? "旧配置存在多个设备且没有唯一协议归属，请人工映射" : "旧配置存在多个同协议设备，请人工映射"));
        return null;
    }

    private static List<PointsConfig.PointEntry> FindSimulationPoints(
        string address,
        IReadOnlyDictionary<string, List<PointsConfig.PointEntry>> byAddress)
        => byAddress.TryGetValue(address.Trim(), out var matches)
            ? matches.Where(point => DevicePointTypeCatalog.TryParseProtocol(point.Protocol, out var protocol)
                                     && protocol == DevicePointProtocol.Simulation).ToList()
            : new List<PointsConfig.PointEntry>();

    private static string NormalizeLegacyDriverKey(string protocol)
        => string.IsNullOrWhiteSpace(protocol)
            ? "legacy-unknown"
            : "legacy-" + new string(protocol.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static PointsConfig.ScaleValues? CloneScale(PointsConfig.ScaleValues? source)
        => source is null ? null : new PointsConfig.ScaleValues
        {
            RawMin = source.RawMin,
            RawMax = source.RawMax,
            EngMin = source.EngMin,
            EngMax = source.EngMax
        };

    private static string BuildRevisionSeed(
        IReadOnlyList<DeviceConfig.DeviceEntry> devices,
        IReadOnlyList<PointsConfig.PointEntry> points,
        SimulationConfig simulation)
        => string.Join("|", devices.Select(device => $"d:{device.Name}:{device.Protocol}:{device.Address}"))
           + "|" + string.Join("|", points.Select(point => $"p:{point.Code}:{point.Protocol}:{point.Address}:{point.DataType}"))
           + "|" + string.Join("|", simulation.InitialValues.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// 从业务稳定字段生成可重复的 GUID，保证迁移重试不会产生另一批身份。
    /// </summary>
    public static string StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var guidBytes = bytes[..16].ToArray();
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes).ToString("D");
    }

    private static string ShortId(string id) => id.Length >= 8 ? id[..8] : id;
}

public sealed record ConfigurationMigrationResult(
    DeviceConfigurationSnapshot Candidate,
    IReadOnlyList<ConfigurationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
