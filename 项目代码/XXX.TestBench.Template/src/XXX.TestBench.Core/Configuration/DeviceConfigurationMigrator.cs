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
            var requestTimeout = Math.Max(50, timeout);
            var staleAfter = (int)Math.Min(int.MaxValue,
                Math.Max(2L * Math.Max(1, poll), requestTimeout + 100L));

            var channel = new ChannelEntry
            {
                Id = channelId,
                Code = "CH_" + ShortId(channelId),
                Name = string.IsNullOrWhiteSpace(source.Name) ? "迁移通道" : source.Name.Trim() + "通道",
                TransportKind = isSimulation ? ChannelTransportKind.Simulation : ChannelTransportKind.Tcp,
                Enabled = source.Enabled,
                TimeoutMs = timeout,
                RetryCount = 0,
                Simulation = isSimulation ? new SimulationChannelParameters
                {
                    InstanceKey = string.IsNullOrWhiteSpace(source.Address) ? ShortId(deviceId) : source.Address.Trim()
                } : null,
                Tcp = isSimulation ? null : new TcpChannelParameters()
            };
            channels.Add(channel);

            var target = new DeviceConfig.DeviceEntry
            {
                Id = deviceId,
                Code = "DEV_" + ShortId(deviceId),
                Name = string.IsNullOrWhiteSpace(source.Name) ? "迁移设备" : source.Name.Trim(),
                DeviceMode = legacyDevice.DeviceMode,
                Protocol = source.Protocol.Trim(),
                Address = source.Address.Trim(),
                ChannelId = channelId,
                DriverKey = driverKey,
                Model = string.Empty,
                PollIntervalMs = Math.Max(1, poll),
                StaleAfterMs = staleAfter,
                ScanMode = DeviceScanMode.FixedInterval,
                Timing = new DeviceTimingOptions
                {
                    ConnectTimeoutMs = Math.Max(1000, timeout),
                    RequestTimeoutMs = requestTimeout,
                    RetryCount = 0
                },
                AutoDemotion = new DeviceDemotionOptions(),
                SiemensS7 = string.Equals(driverKey, DriverKeyCatalog.SiemensS7, StringComparison.OrdinalIgnoreCase)
                    ? new SiemensS7ConnectionOptions
                    {
                        Host = source.Address.Trim(),
                        Port = 102,
                        Rack = 0,
                        Slot = 0
                    }
                    : null,
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
                IsWritable = source.IsWritable,
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

    /// <summary>
    /// 将设备 schema 3 迁移为设备 schema 4：把 S7 目标端点、连接时序、采集策略和降级策略
    /// 收拢到设备项，并清空共享 TCP 通道中的远端 Host/Port。设备和点位身份保持不变。
    /// </summary>
    public static ConfigurationMigrationResult MigrateDeviceToV4(
        DeviceConfigurationSnapshot source,
        string? revision = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Device is null || source.Points is null || source.Simulation is null || source.SignalBindings is null)
            return new ConfigurationMigrationResult(source, new[]
            {
                new ConfigurationIssue("snapshot", "迁移源的完整配置对象不能为空")
            });
        if (source.Device.SchemaVersion == DeviceConfig.CurrentSchemaVersion)
            return new ConfigurationMigrationResult(source, Array.Empty<ConfigurationIssue>());
        if (source.Device.SchemaVersion != DeviceConfig.PreviousSchemaVersion)
            return new ConfigurationMigrationResult(source, new[]
            {
                new ConfigurationIssue("device.json.schemaVersion", $"只支持从 schema {DeviceConfig.PreviousSchemaVersion} 迁移")
            });

        var sourceChannels = source.Device.Channels ?? new List<ChannelEntry>();
        var channels = sourceChannels.Select(CloneChannelForV4).ToList();
        var channelMap = sourceChannels
            .Where(channel => channel is not null && !string.IsNullOrWhiteSpace(channel.Id))
            .GroupBy(channel => channel.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var devices = new List<DeviceConfig.DeviceEntry>();
        foreach (var sourceDevice in source.Device.Devices ?? new List<DeviceConfig.DeviceEntry>())
        {
            if (sourceDevice is null)
                continue;
            channelMap.TryGetValue(sourceDevice.ChannelId.Trim(), out var channel);
            var legacyHost = !string.IsNullOrWhiteSpace(sourceDevice.Address)
                ? sourceDevice.Address.Trim()
                : channel?.Tcp?.Host?.Trim() ?? string.Empty;
            var legacyPort = channel?.Tcp?.Port is > 0 and <= 65535 ? channel.Tcp.Port : 102;
            var poll = Math.Max(1, sourceDevice.PollIntervalMs > 0 ? sourceDevice.PollIntervalMs : source.Device.PollIntervalMs);
            var legacyTimeout = channel?.TimeoutMs > 0
                ? channel.TimeoutMs
                : source.Device.TimeoutMs > 0 ? source.Device.TimeoutMs : 1000;
            var connectTimeout = Math.Max(1000, legacyTimeout);
            var requestTimeout = Math.Max(50, legacyTimeout);
            var staleAfter = (int)Math.Min(int.MaxValue,
                Math.Max(2L * poll, requestTimeout + 100L));
            var isS7 = string.Equals(sourceDevice.DriverKey, DriverKeyCatalog.SiemensS7, StringComparison.OrdinalIgnoreCase);
            var isModbusTcp = string.Equals(sourceDevice.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase);
            devices.Add(new DeviceConfig.DeviceEntry
            {
                Id = sourceDevice.Id,
                Code = sourceDevice.Code,
                Name = sourceDevice.Name,
                DeviceMode = sourceDevice.DeviceMode,
                Protocol = sourceDevice.Protocol,
                Address = string.Empty,
                ChannelId = sourceDevice.ChannelId,
                DriverKey = sourceDevice.DriverKey,
                Model = sourceDevice.Model,
                ModbusUnitId = sourceDevice.ModbusUnitId,
                PollIntervalMs = poll,
                StaleAfterMs = staleAfter,
                ScanMode = DeviceScanMode.FixedInterval,
                Timing = new DeviceTimingOptions
                {
                    ConnectTimeoutMs = connectTimeout,
                    RequestTimeoutMs = requestTimeout,
                    RetryCount = Math.Max(0, channel?.RetryCount ?? 0),
                    InterRequestDelayMs = 0
                },
                AutoDemotion = new DeviceDemotionOptions(),
                SiemensS7 = isS7
                    ? new SiemensS7ConnectionOptions
                    {
                        Host = legacyHost,
                        Port = legacyPort,
                        Rack = 0,
                        Slot = 0
                    }
                    : null,
                ModbusTcp = isModbusTcp
                    ? new ModbusTcpConnectionOptions
                    {
                        Host = legacyHost,
                        Port = legacyPort
                    }
                    : null,
                Enabled = sourceDevice.Enabled
            });
        }

        var devicesById = devices.ToDictionary(device => device.Id.Trim(), StringComparer.OrdinalIgnoreCase);
        var groups = source.Points.SchemaVersion == PointsConfig.CurrentSchemaVersion
            ? (source.Points.Groups ?? new List<PointsConfig.PointGroupEntry>()).Select(group => new PointsConfig.PointGroupEntry
            {
                Id = group.Id,
                DeviceId = group.DeviceId,
                Code = group.Code,
                Name = group.Name,
                Description = group.Description,
                SortOrder = group.SortOrder
            }).ToList()
            : devices.Select(device => new PointsConfig.PointGroupEntry
            {
                Id = StableId($"group|{device.Id}|DEFAULT"),
                DeviceId = device.Id,
                Code = "DEFAULT",
                Name = "未分组",
                Description = "由设备 schema 3 迁移生成的默认分组",
                SortOrder = 0
            }).ToList();
        var defaultGroupByDevice = groups
            .GroupBy(group => group.DeviceId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        var points = (source.Points.Points ?? new List<PointsConfig.PointEntry>())
            .Where(point => point is not null)
            .Select(point => ClonePoint(point, defaultGroupByDevice))
            .ToList();

        var candidate = new DeviceConfigurationSnapshot
        {
            SchemaVersion = DeviceConfigurationSnapshot.CurrentSchemaVersion,
            Revision = revision ?? "migration-device-v4-" + ShortId(StableId(
                source.Revision + "|" + string.Join("|", devices.Select(device => device.Id))
                + "|" + string.Join("|", points.Select(point => point.Id)))),
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                DeviceMode = source.Device.DeviceMode,
                PollIntervalMs = source.Device.PollIntervalMs,
                TimeoutMs = source.Device.TimeoutMs,
                Channels = channels,
                Devices = devices
            },
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

    private static ChannelEntry CloneChannelForV4(ChannelEntry source)
        => new()
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            TransportKind = source.TransportKind,
            Enabled = source.Enabled,
            TimeoutMs = source.TimeoutMs,
            RetryCount = source.RetryCount,
            Tcp = source.Tcp is null ? null : new TcpChannelParameters
            {
                Host = string.Empty,
                Port = 0,
                LocalInterface = source.Tcp.LocalInterface
            },
            Serial = source.Serial is null ? null : new SerialChannelParameters
            {
                PortName = source.Serial.PortName,
                BaudRate = source.Serial.BaudRate,
                DataBits = source.Serial.DataBits,
                Parity = source.Serial.Parity,
                StopBits = source.Serial.StopBits
            },
            Simulation = source.Simulation is null ? null : new SimulationChannelParameters
            {
                InstanceKey = source.Simulation.InstanceKey
            }
        };

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
            IsWritable = source.IsWritable,
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
