using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Upgrade;

internal sealed record ConfigurationUpgradePlan(
    DeviceConfigurationSnapshot? Snapshot,
    string SourceVersions,
    string OutputVersions,
    bool NoChange,
    IReadOnlyDictionary<string, long> BeforeCounts,
    IReadOnlyDictionary<string, long> AfterCounts,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers,
    int FailureCode,
    string ContentHash);

/// <summary>Pure, allow-listed conversion from the explicitly supported configuration versions.</summary>
internal static class ConfigurationUpgradePlanner
{
    private static readonly JsonSerializerOptions CurrentJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static ConfigurationUpgradePlan Plan(LegacyConfigurationInput input)
    {
        var changes = new List<string>();
        var warnings = new List<string>();
        var blockers = new List<string>();
        var deviceNode = ParseObject(input.Files["device.json"], "device.json");
        var pointsNode = ParseObject(input.Files["points.json"], "points.json");
        var simulationNode = ParseObject(input.Files["simulation.json"], "simulation.json");
        var bindingsNode = ParseObject(input.Files["signal-bindings.json"], "signal-bindings.json");
        var deviceVersion = GetVersion(deviceNode, "device.json");
        var pointsVersion = GetVersion(pointsNode, "points.json");
        var simulationVersion = GetVersion(simulationNode, "simulation.json");
        var sourceVersions = $"device={deviceVersion};points={pointsVersion};simulation={simulationVersion};bindings={(input.HasSignalBindings ? "1" : "synthesized-empty-v1")}";

        if (deviceVersion == DeviceConfig.CurrentSchemaVersion
            && pointsVersion == PointsConfig.CurrentSchemaVersion
            && simulationVersion == SimulationConfig.CurrentSchemaVersion)
        {
            if (input.HasSignalBindings && GetVersion(bindingsNode, "signal-bindings.json") != SignalBindingsConfig.CurrentSchemaVersion)
                throw new UpgradeCommandException(2, "signal-bindings.json 版本不受支持");
            var current = DeserializeSnapshot(deviceNode, pointsNode, simulationNode, bindingsNode, "validation", blockers);
            if (blockers.Count == 0)
                ValidateSnapshot(current, blockers);
            var counts = CountRows(current);
            if (blockers.Count > 0)
                return new(null, sourceVersions, "device=5;points=4;simulation=3;bindings=1", true,
                    counts, new Dictionary<string, long>(), changes, warnings, blockers, 3, input.CombinedHash);
            warnings.AddRange(MissingBindingsWarning(input));
            changes.Add("NoChange：输入已是全部目标版本且语义校验通过，不创建新 revision");
            return new(current, sourceVersions, "device=5;points=4;simulation=3;bindings=1", true,
                counts, counts, changes, warnings, blockers, 0, input.CombinedHash);
        }

        var supported = deviceVersion is 3 or 4
            && pointsVersion is 2 or 3
            && simulationVersion == 2;
        if (!supported)
            throw new UpgradeCommandException(2,
                $"不支持混合配置版本：{sourceVersions}；仅支持 device v3/v4 + points v2/v3 + simulation v2，或全部为当前版本");
        if (input.HasSignalBindings && GetVersion(bindingsNode, "signal-bindings.json") != SignalBindingsConfig.CurrentSchemaVersion)
            throw new UpgradeCommandException(2, "旧配置中的 signal-bindings.json 必须为 v1");

        var convertedDevice = ConvertDevice(deviceNode, deviceVersion, changes, warnings, blockers);
        var convertedPoints = ConvertPoints(pointsNode, pointsVersion, convertedDevice, changes, blockers);
        var convertedSimulation = ConvertSimulation(simulationNode, convertedPoints, changes, blockers);
        if (!input.HasSignalBindings)
        {
            warnings.Add("独立文件输入缺少 signal-bindings.json；输出空绑定。执行器必需信号未配置，试验启动将被拒绝");
            changes.Add("生成空 signal-bindings.json v1，不按点位名称自动绑定");
        }

        var snapshot = DeserializeSnapshot(convertedDevice, convertedPoints, convertedSimulation, bindingsNode, "validation", blockers);
        if (blockers.Count == 0)
            ValidateSnapshot(snapshot, blockers);
        var beforeCounts = CountRows(deviceNode, pointsNode, simulationNode, bindingsNode);
        var afterCounts = CountRows(snapshot);
        if (blockers.Count > 0)
            return new(null, sourceVersions, "device=5;points=4;simulation=3;bindings=1", false,
                beforeCounts, new Dictionary<string, long>(), changes, warnings, blockers, 3, input.CombinedHash);

        NormalizeDictionaryOrder(snapshot);
        var contentHash = HashSnapshot(snapshot);
        snapshot.Revision = "sha256-" + contentHash;
        return new(snapshot, sourceVersions, "device=5;points=4;simulation=3;bindings=1", false,
            beforeCounts, afterCounts, changes, warnings, blockers, 0, contentHash);
    }

    private static JsonObject ConvertDevice(JsonObject source, int version, ICollection<string> changes,
        ICollection<string> warnings, ICollection<string> blockers)
    {
        var result = (JsonObject)source.DeepClone();
        var devicesNode = RequireArray(result, "devices", "device.json.devices");
        var channelsNode = RequireArray(result, "channels", "device.json.channels");
        var channelById = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        var legacyEndpointByChannel = new Dictionary<string, (string? Host, int? Port)>(StringComparer.OrdinalIgnoreCase);
        foreach (var channelNode in channelsNode)
        {
            var channel = RequireObject(channelNode, "device.json.channels");
            var id = RequiredString(channel, "id", "device.json.channels[].id");
            if (!Guid.TryParse(id, out _))
                throw new UpgradeCommandException(3, $"通道持久化 Id 无效：{id}");
            if (!channelById.TryAdd(id, channel))
                throw new UpgradeCommandException(3, $"存在重复通道 Id：{id}");
            var tcp = GetObject(channel, "tcp");
            if (tcp is not null)
            {
                var legacyHost = OptionalString(tcp, "host");
                var legacyPort = GetOptionalInt(tcp, "port");
                legacyEndpointByChannel[id] = (legacyHost, legacyPort);
                if (version == 3 || version == 4)
                {
                    if (Get(tcp, "host") is not null || Get(tcp, "port") is not null)
                        changes.Add($"通道 {id} 的 TCP 远端地址移至对应设备专属端点");
                    Remove(tcp, "host");
                    Remove(tcp, "port");
                }
            }
            else
            {
                legacyEndpointByChannel[id] = (null, null);
            }
        }

        var topMode = Get(result, "deviceMode");
        var topPoll = Get(result, "pollIntervalMs");
        var topTimeout = Get(result, "timeoutMs");
        var convertedDevices = new JsonArray();
        foreach (var deviceNode in devicesNode)
        {
            var device = (JsonObject)RequireObject(deviceNode, "device.json.devices[]").DeepClone();
            var code = OptionalString(device, "code") ?? "(unknown)";
            var id = RequiredString(device, "id", $"device.json.devices[{code}].id");
            if (!Guid.TryParse(id, out _))
                throw new UpgradeCommandException(3, $"设备 {code} 的持久化 Id 无效：{id}");
            var channelId = RequiredString(device, "channelId", $"device.json.devices[{code}].channelId");
            if (!channelById.TryGetValue(channelId, out var channel))
                throw new UpgradeCommandException(3, $"设备 {code} 引用的通道不存在：{channelId}");

            var oldMode = Get(device, "deviceMode");
            if (oldMode is null)
            {
                if (topMode is null)
                    throw new UpgradeCommandException(3, $"设备 {code} 缺少 deviceMode，且没有可用的旧全局模式");
                Set(device, "deviceMode", topMode.DeepClone());
            }
            else if (topMode is not null && !EnumTextEquals(oldMode, topMode))
            {
                throw new UpgradeCommandException(3, $"设备 {code} 的 deviceMode 与旧全局 deviceMode 冲突");
            }

            var driverKey = ResolveDriverKey(device, code);
            Set(device, "driverKey", JsonValue.Create(driverKey));
            var tcp = GetObject(channel, "tcp");
            var oldAddress = OptionalString(device, "address");
            var legacyChannelEndpoint = legacyEndpointByChannel[channelId];
            var oldHost = legacyChannelEndpoint.Host;
            var s7Node = GetObject(device, "siemensS7");
            var modbusNode = GetObject(device, "modbusTcp");
            var isS7 = driverKey == DriverKeyCatalog.SiemensS7;
            var isModbusTcp = driverKey == DriverKeyCatalog.ModbusTcp;

            if (isS7)
            {
                var endpoint = ResolveEndpoint(code, "Siemens S7", oldAddress, oldHost, s7Node,
                    legacyChannelEndpoint.Port, 102);
                if (string.IsNullOrWhiteSpace(endpoint.Host))
                    throw new UpgradeCommandException(3, $"设备 {code} 缺少可无歧义恢复的 Siemens S7 Host");
                if (version == 3)
                {
                    var rack = GetOptionalInt(s7Node, "rack") ?? 0;
                    var slot = GetOptionalInt(s7Node, "slot") ?? 0;
                    if (rack != 0 || slot != 0)
                        throw new UpgradeCommandException(3, $"device v3 设备 {code} 显式 Rack/Slot 与已知旧迁移结果 0/0 冲突");
                    ApplyV3Timing(device, channel, code, topPoll, topTimeout, changes, warnings, blockers);
                    Set(device, "siemensS7", CreateS7(endpoint.Host, endpoint.Port, 0, 0));
                }
                else
                {
                    Set(device, "siemensS7", CreateS7(endpoint.Host, endpoint.Port,
                        GetOptionalInt(s7Node, "rack") ?? 0, GetOptionalInt(s7Node, "slot") ?? 0));
                }
            }
            else if (isModbusTcp)
            {
                var endpoint = ResolveEndpoint(code, "Modbus TCP", oldAddress, oldHost, modbusNode,
                    legacyChannelEndpoint.Port, 502);
                if (!string.IsNullOrWhiteSpace(endpoint.Host))
                    Set(device, "modbusTcp", CreateModbusTcp(endpoint.Host, endpoint.Port));
                if (version == 3)
                    ApplyV3Timing(device, channel, code, topPoll, topTimeout, changes, warnings, blockers);
            }
            else
            {
                if (version == 3)
                    ApplyV3Timing(device, channel, code, topPoll, topTimeout, changes, warnings, blockers);
                Remove(device, "siemensS7");
                Remove(device, "modbusTcp");
            }

            var oldEnabled = Get(device, "enabled");
            if (oldEnabled is not null)
            {
                if (TryBoolean(oldEnabled, out var enabled) && !enabled)
                    warnings.Add($"设备 {code} 的旧 enabled=false 被删除；当前运行时不以该字段禁用设备");
                else
                    changes.Add($"设备 {code} 的旧 enabled 字段移除");
            }
            var demotion = GetObject(device, "autoDemotion");
            if (demotion is not null && Remove(demotion, "discardWritesWhileDemoted", out var discard))
                warnings.Add($"设备 {code} 的 discardWritesWhileDemoted={NodeText(discard)} 已删除；降级期间仍固定禁写");

            Remove(device, "protocol");
            Remove(device, "address");
            Remove(device, "enabled");
            convertedDevices.Add(device);
        }

        Set(result, "schemaVersion", JsonValue.Create(DeviceConfig.CurrentSchemaVersion));
        Set(result, "devices", convertedDevices);
        Remove(result, "deviceMode");
        Remove(result, "pollIntervalMs");
        Remove(result, "timeoutMs");
        changes.Add($"device.json schema {version} → {DeviceConfig.CurrentSchemaVersion}");
        return result;
    }

    private static void ApplyV3Timing(JsonObject device, JsonObject channel, string code,
        JsonNode? topPoll, JsonNode? topTimeout, ICollection<string> changes,
        ICollection<string> warnings, ICollection<string> blockers)
    {
        var devicePollNode = Get(device, "pollIntervalMs");
        var channelTimeoutNode = Get(channel, "timeoutMs");
        var channelRetryNode = Get(channel, "retryCount");
        var devicePoll = ReadOptionalPositiveInt(devicePollNode, $"设备 {code} pollIntervalMs");
        var globalPoll = ReadOptionalPositiveInt(topPoll, "旧全局 pollIntervalMs");
        var poll = devicePoll is > 0 ? devicePoll.Value : globalPoll is > 0 ? globalPoll.Value : 500;
        if (devicePoll is not > 0 && globalPoll is not > 0)
            warnings.Add($"设备 {code} pollIntervalMs 缺失，按旧设备配置 DTO 默认值 500 ms 还原");

        var channelTimeout = ReadRequiredPositiveIntIfPresent(channelTimeoutNode, $"通道 {RequiredString(channel, "id", "channel.id")} timeoutMs");
        var globalTimeout = ReadRequiredPositiveIntIfPresent(topTimeout, "旧全局 timeoutMs");
        var timeout = channelTimeout ?? globalTimeout ?? 1000;
        if (channelTimeout is null && globalTimeout is null)
            warnings.Add($"设备 {code} 的超时字段缺失，按旧配置已有默认值 1000 ms 还原");
        var retry = ReadOptionalNonNegativeInt(channelRetryNode, $"通道 {RequiredString(channel, "id", "channel.id")} retryCount") ?? 0;
        var requestTimeout = Math.Max(50, timeout);
        var connectTimeout = Math.Max(1000, timeout);
        var staleAfter = (int)Math.Min(int.MaxValue, Math.Max(2L * poll, requestTimeout + 100L));

        CheckLaterTimingField(GetObject(device, "timing"), "connectTimeoutMs", connectTimeout, code, blockers);
        CheckLaterTimingField(GetObject(device, "timing"), "requestTimeoutMs", requestTimeout, code, blockers);
        CheckLaterTimingField(GetObject(device, "timing"), "retryCount", retry, code, blockers);
        CheckLaterTimingField(GetObject(device, "timing"), "interRequestDelayMs", 0, code, blockers);
        var scan = Get(device, "scanMode");
        if (scan is not null && !string.Equals(OptionalString(device, "scanMode"), "FixedInterval", StringComparison.OrdinalIgnoreCase))
            blockers.Add($"device v3 设备 {code} 含与已知迁移结果不符的 scanMode");
        var auto = GetObject(device, "autoDemotion");
        CheckLaterTimingField(auto, "enabled", true, code, blockers);
        CheckLaterTimingField(auto, "failureThreshold", 3, code, blockers);
        CheckLaterTimingField(auto, "demotionPeriodMs", 10000, code, blockers);
        if (Get(device, "staleAfterMs") is { } oldStale && TryInt32(oldStale, out var staleValue) && staleValue != staleAfter)
            warnings.Add($"设备 {code} staleAfterMs：旧文件 {staleValue} → 上版启动有效值 {staleAfter} → 新值 {staleAfter} ms");

        Set(device, "pollIntervalMs", JsonValue.Create(poll));
        Set(device, "staleAfterMs", JsonValue.Create(staleAfter));
        Set(device, "scanMode", JsonValue.Create("FixedInterval"));
        Set(device, "timing", new JsonObject
        {
            ["connectTimeoutMs"] = connectTimeout,
            ["requestTimeoutMs"] = requestTimeout,
            ["retryCount"] = retry,
            ["interRequestDelayMs"] = 0
        });
        Set(device, "autoDemotion", new JsonObject
        {
            ["enabled"] = true,
            ["failureThreshold"] = 3,
            ["demotionPeriodMs"] = 10000
        });
        changes.Add($"device v3 设备 {code} 时序按旧启动公式还原：poll={poll} ms、connect={connectTimeout} ms、request={requestTimeout} ms、stale={staleAfter} ms");
    }

    private static JsonObject ConvertPoints(JsonObject source, int version, JsonObject convertedDevice,
        ICollection<string> changes, ICollection<string> blockers)
    {
        var result = (JsonObject)source.DeepClone();
        var devices = RequireArray(convertedDevice, "devices", "device.json.devices");
        var deviceIdsInOrder = devices.Select(node => RequiredString(RequireObject(node, "device"), "id", "device.id"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var deviceIds = deviceIdsInOrder.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pointArray = RequireArray(result, "points", "points.json.points");
        var groups = new JsonArray();
        if (version == 2)
        {
            if (Get(result, "groups") is not null)
                throw new UpgradeCommandException(2, "points v2 不应包含 groups；拒绝混合版本结构");
            var groupIdByDevice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var deviceId in deviceIdsInOrder)
            {
                var groupId = StableIdentity.FromSeed($"group|{deviceId}|DEFAULT");
                groupIdByDevice[deviceId] = groupId;
                groups.Add(new JsonObject
                {
                    ["id"] = groupId,
                    ["deviceId"] = deviceId,
                    ["code"] = "DEFAULT",
                    ["name"] = "未分组",
                    ["description"] = "由点位 v2 转换生成的设备默认分组",
                    ["sortOrder"] = 0
                });
            }
            foreach (var pointNode in pointArray)
            {
                var point = RequireObject(pointNode, "points.json.points[]");
                var deviceId = RequiredString(point, "deviceId", "points.json.points[].deviceId");
                if (!groupIdByDevice.TryGetValue(deviceId, out var groupId))
                    throw new UpgradeCommandException(3, $"点位 {OptionalString(point, "code") ?? "(unknown)"} 引用了不存在的设备 Id：{deviceId}");
                Set(point, "groupId", JsonValue.Create(groupId));
            }
            changes.Add("points v2：每台设备生成稳定 DEFAULT 分组，点位按已有 DeviceId 归组");
        }
        else
        {
            var sourceGroups = RequireArray(result, "groups", "points.json.groups");
            groups = (JsonArray)sourceGroups.DeepClone();
        }

        foreach (var pointNode in pointArray)
        {
            var point = RequireObject(pointNode, "points.json.points[]");
            var code = OptionalString(point, "code") ?? "(unknown)";
            var rawDataType = Get(point, "rawDataType");
            var dataTypeText = OptionalString(point, "dataType");
            var rawDataTypeText = rawDataType is null ? null : NodeString(rawDataType, $"点位 {code} rawDataType");
            var dataType = ParseDataType(dataTypeText ?? rawDataTypeText, $"点位 {code} dataType");
            if (rawDataTypeText is not null)
            {
                var rawParsed = ParseDataType(rawDataTypeText, $"点位 {code} rawDataType");
                if (rawParsed != dataType)
                    throw new UpgradeCommandException(3, $"点位 {code} 的 dataType 与 rawDataType 冲突：{dataType}/{rawParsed}");
                Remove(point, "rawDataType");
                changes.Add($"点位 {code} 相同类型的 rawDataType 合并为 dataType");
            }
            Set(point, "dataType", JsonValue.Create(DevicePointTypeCatalog.ToStorage(dataType)));

            var flatNames = new[] { "rawMin", "rawMax", "engMin", "engMax" };
            var flat = flatNames.Select(name => TryDecimal(Get(point, name), out var value) ? value : (decimal?)null).ToArray();
            var flatPresent = flatNames.Count(name => Get(point, name) is { } node && node.GetValueKind() != JsonValueKind.Null);
            if (flatPresent is > 0 and < 4)
                throw new UpgradeCommandException(3, $"点位 {code} 的平铺量程不完整");
            var scaleNode = Get(point, "scale");
            if (scaleNode is not null && scaleNode.GetValueKind() != System.Text.Json.JsonValueKind.Null)
            {
                var scale = RequireObject(scaleNode, $"点位 {code}.scale");
                var scaleNames = new[] { "rawMin", "rawMax", "engMin", "engMax" };
                var unknownScaleFields = scale.Select(pair => pair.Key)
                    .Where(name => !scaleNames.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
                if (unknownScaleFields.Length > 0)
                    throw new UpgradeCommandException(2, $"点位 {code} 的 Scale 含未登记字段：{string.Join(",", unknownScaleFields)}");
                var scaleValues = scaleNames.Select(name => TryDecimal(Get(scale, name), out var value) ? value : (decimal?)null).ToArray();
                if (scaleNames.Any(name => !Has(scale, name)) || scaleValues.Any(value => !value.HasValue))
                    throw new UpgradeCommandException(3, $"点位 {code} 的 Scale 不完整");
                if (flatPresent == 4 && !flat.SequenceEqual(scaleValues))
                    throw new UpgradeCommandException(3, $"点位 {code} 的平铺量程与 Scale 冲突");
                if (flatPresent == 0)
                    for (var index = 0; index < flatNames.Length; index++)
                        Set(point, flatNames[index], JsonValue.Create(scaleValues[index]!.Value));
                Remove(point, "scale");
                changes.Add($"点位 {code} 的 Scale 拆为四个平铺量程字段");
            }
            else if (scaleNode is not null)
            {
                Remove(point, "scale");
            }

            if (!Guid.TryParse(OptionalString(point, "id"), out _))
                throw new UpgradeCommandException(3, $"点位 {code} 缺少有效持久化 Id，禁止生成替代身份");
        }

        Set(result, "schemaVersion", JsonValue.Create(PointsConfig.CurrentSchemaVersion));
        Set(result, "groups", groups);
        changes.Add($"points.json schema {version} → {PointsConfig.CurrentSchemaVersion}");
        return result;
    }

    private static JsonObject ConvertSimulation(JsonObject source, JsonObject convertedPoints,
        ICollection<string> changes, ICollection<string> blockers)
    {
        var result = (JsonObject)source.DeepClone();
        var pointsById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in RequireArray(convertedPoints, "points", "points.json.points"))
        {
            var point = RequireObject(item, "points.json.points[]");
            var id = RequiredString(point, "id", "points.json.points[].id");
            pointsById[id] = RequiredString(point, "address", $"points.json.points[{id}].address");
        }

        var oldInitialValues = Get(result, "initialValues");
        if (oldInitialValues is not null)
        {
            if (oldInitialValues.GetValueKind() != System.Text.Json.JsonValueKind.Object
                || ((JsonObject)oldInitialValues).Count != 0)
                throw new UpgradeCommandException(3, "simulation v2 initialValues 不是空对象；地址字典不能跨设备猜测 PointId");
            Remove(result, "initialValues");
            changes.Add("删除空的旧 initialValues 地址字典");
        }

        ValidateInitialValues(result, pointsById);
        foreach (var rule in OptionalArray(result, "changeRules"))
            RemoveAndVerifyAddress(RequireObject(rule, "simulation.json.changeRules[]"), pointsById, "变化规则");
        foreach (var fault in OptionalArray(result, "faultInjectionScenarios"))
            RemoveAndVerifyAddress(RequireObject(fault, "simulation.json.faultInjectionScenarios[]"), pointsById, "故障场景");

        Set(result, "schemaVersion", JsonValue.Create(SimulationConfig.CurrentSchemaVersion));
        changes.Add($"simulation.json schema 2 → {SimulationConfig.CurrentSchemaVersion}");
        return result;
    }

    private static void RemoveAndVerifyAddress(JsonObject item, IReadOnlyDictionary<string, string> pointsById, string kind)
    {
        var pointId = RequiredString(item, "pointId", $"simulation.json.{kind}.pointId");
        if (!pointsById.TryGetValue(pointId, out var pointAddress))
            throw new UpgradeCommandException(3, $"simulation.json {kind} 引用不存在的 PointId：{pointId}");
        var legacyAddress = OptionalString(item, "address");
        if (!string.IsNullOrWhiteSpace(legacyAddress)
            && !string.Equals(legacyAddress.Trim(), pointAddress.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new UpgradeCommandException(3, $"simulation.json {kind} 的 Address 与 PointId {pointId} 指向不同点位");
        Remove(item, "address");
    }

    private static void ValidateInitialValues(JsonObject simulation, IReadOnlyDictionary<string, string> pointsById)
    {
        if (Get(simulation, "initialValuesByPointId") is not { } node)
            return;
        var values = RequireObject(node, "simulation.json.initialValuesByPointId");
        foreach (var pair in values)
            if (!Guid.TryParse(pair.Key, out _) || !pointsById.ContainsKey(pair.Key))
                throw new UpgradeCommandException(3, $"simulation 初值引用不存在或无效的 PointId：{pair.Key}");
    }

    private static DeviceConfigurationSnapshot DeserializeSnapshot(JsonObject deviceNode, JsonObject pointsNode,
        JsonObject simulationNode, JsonObject bindingsNode, string revision, ICollection<string> blockers)
    {
        try
        {
            var device = Deserialize<DeviceConfig>(deviceNode, "device.json");
            var points = Deserialize<PointsConfig>(pointsNode, "points.json");
            var simulation = Deserialize<SimulationConfig>(simulationNode, "simulation.json");
            var bindings = Deserialize<SignalBindingsConfig>(bindingsNode, "signal-bindings.json");
            return new DeviceConfigurationSnapshot
            {
                Revision = revision,
                Device = device,
                Points = points,
                Simulation = simulation,
                SignalBindings = bindings
            };
        }
        catch (UpgradeCommandException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new UpgradeCommandException(2, $"当前目标结构无效或存在未登记字段：{ex.Message}");
        }
    }

    private static T Deserialize<T>(JsonObject node, string file) where T : class
        => JsonSerializer.Deserialize<T>(node.ToJsonString(), CurrentJson)
           ?? throw new UpgradeCommandException(2, $"{file} 根对象为空");

    private static void ValidateSnapshot(DeviceConfigurationSnapshot snapshot, ICollection<string> blockers)
    {
        try
        {
            snapshot.Device.Validate();
            snapshot.Points.Validate();
            snapshot.Simulation.Validate();
            var pointIds = snapshot.Points.Points.Select(point => point.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var bindingIssues = snapshot.SignalBindings.Validate(pointIds);
            if (bindingIssues.Count > 0)
                throw new ConfigValidationException(string.Join("；", bindingIssues));
            var issues = MultiDeviceConfigurationValidator.Validate(snapshot);
            if (issues.Count > 0)
                throw new ConfigValidationException(string.Join("；", issues));
        }
        catch (Exception ex) when (ex is ConfigValidationException or ArgumentException or InvalidOperationException)
        {
            blockers.Add("目标配置语义校验失败：" + ex.Message);
        }
    }

    private static JsonObject ParseObject(byte[] bytes, string file)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            ValidateNoDuplicateProperties(document.RootElement, file);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new UpgradeCommandException(2, $"{file} 根必须为 JSON 对象");
            return JsonNode.Parse(bytes)!.AsObject();
        }
        catch (UpgradeCommandException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new UpgradeCommandException(2, $"{file} JSON 无效：{ex.Message}");
        }
    }

    private static void ValidateNoDuplicateProperties(JsonElement element, string path)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw new UpgradeCommandException(2, $"{path} 存在重复 JSON 字段：{property.Name}");
                ValidateNoDuplicateProperties(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                ValidateNoDuplicateProperties(item, path + "[]");
    }

    private static int GetVersion(JsonObject root, string file)
    {
        if (Get(root, "schemaVersion") is not { } node || !TryInt32(node, out var value))
            throw new UpgradeCommandException(2, $"{file}.schemaVersion 必须显式提供整数版本号");
        return value;
    }

    private static void CheckLaterTimingField(JsonObject? source, string field, object expected, string code, ICollection<string> blockers)
    {
        var node = source is null ? null : Get(source, field);
        if (node is null)
            return;
        var matches = expected switch
        {
            int integer => TryInt32(node, out var value) && value == integer,
            bool boolean => TryBoolean(node, out var value) && value == boolean,
            _ => false
        };
        if (!matches)
            blockers.Add($"device v3 设备 {code} 含与已知 v3 运行行为冲突的 {field}");
    }

    private static string ResolveDriverKey(JsonObject device, string code)
    {
        var fromDriver = OptionalString(device, "driverKey");
        var protocol = OptionalString(device, "protocol");
        DevicePointProtocol protocolKind = DevicePointProtocol.Unknown;
        if (!string.IsNullOrWhiteSpace(protocol) && !DevicePointTypeCatalog.TryParseProtocol(protocol, out protocolKind))
            throw new UpgradeCommandException(2, $"设备 {code} 的 protocol 不受支持：{protocol}");
        DevicePointProtocol driverKind = DevicePointProtocol.Unknown;
        if (!string.IsNullOrWhiteSpace(fromDriver) && !DevicePointTypeCatalog.TryParseDriverKey(fromDriver, out driverKind))
            throw new UpgradeCommandException(2, $"设备 {code} 的 driverKey 不受支持：{fromDriver}");
        if (protocolKind != DevicePointProtocol.Unknown && driverKind != DevicePointProtocol.Unknown && protocolKind != driverKind)
            throw new UpgradeCommandException(3, $"设备 {code} 的 protocol 与 driverKey 冲突");
        var kind = driverKind != DevicePointProtocol.Unknown ? driverKind : protocolKind;
        return kind switch
        {
            DevicePointProtocol.SiemensS7 => DriverKeyCatalog.SiemensS7,
            DevicePointProtocol.ModbusTcp => DriverKeyCatalog.ModbusTcp,
            DevicePointProtocol.ModbusRtu => DriverKeyCatalog.ModbusRtu,
            DevicePointProtocol.Simulation => DriverKeyCatalog.Simulation,
            _ => throw new UpgradeCommandException(3, $"设备 {code} 缺少可识别的 DriverKey/protocol")
        };
    }

    private static (string Host, int Port) ResolveEndpoint(string code, string label, string? oldAddress,
        string? oldHost, JsonObject? endpoint, int? oldPort, int defaultPort)
    {
        var endpointHost = OptionalString(endpoint, "host");
        var endpointPortNode = endpoint is null ? null : Get(endpoint, "port");
        int? endpointPort = endpointPortNode is null ? null : ReadRequiredPositiveIntIfPresent(endpointPortNode, $"设备 {code} {label}.port");
        if (endpoint is not null && endpointPort is null)
            endpointPort = defaultPort;
        var hostValues = new[] { oldAddress, oldHost, endpointHost }.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hostValues.Length > 1)
            throw new UpgradeCommandException(3, $"设备 {code} 的 {label} Host/Address 来源互相冲突：{string.Join(" / ", hostValues)}");
        var host = hostValues.FirstOrDefault() ?? string.Empty;
        var ports = new[] { oldPort, endpointPort }.Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        if (ports.Length > 1)
            throw new UpgradeCommandException(3, $"设备 {code} 的 {label} 端口来源互相冲突：{string.Join(" / ", ports)}");
        var port = ports.FirstOrDefault(defaultPort);
        if (port is < 1 or > 65535)
            throw new UpgradeCommandException(3, $"设备 {code} 的 {label} 端口无效：{port}");
        return (host, port);
    }

    private static JsonObject CreateS7(string host, int port, int rack, int slot)
        => new() { ["host"] = host, ["port"] = port, ["rack"] = rack, ["slot"] = slot };

    private static JsonObject CreateModbusTcp(string host, int port)
        => new() { ["host"] = host, ["port"] = port };

    private static DevicePointDataType ParseDataType(string? text, string path)
    {
        if (DevicePointTypeCatalog.TryParseDataType(text, out var value))
            return value;
        throw new UpgradeCommandException(2, $"{path} 类型不受支持：{text ?? "缺失"}");
    }

    private static IReadOnlyDictionary<string, long> CountRows(DeviceConfigurationSnapshot snapshot)
        => new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["devices"] = snapshot.Device.Devices.Count,
            ["channels"] = snapshot.Device.Channels.Count,
            ["points"] = snapshot.Points.Points.Count,
            ["groups"] = snapshot.Points.Groups.Count,
            ["initialValues"] = snapshot.Simulation.InitialValuesByPointId.Count,
            ["changeRules"] = snapshot.Simulation.ChangeRules.Count,
            ["faultInjectionScenarios"] = snapshot.Simulation.FaultInjectionScenarios.Count,
            ["signalBindings"] = snapshot.SignalBindings.Bindings.Count
        };

    private static IReadOnlyDictionary<string, long> CountRows(JsonObject device, JsonObject points,
        JsonObject simulation, JsonObject bindings)
    {
        long Count(JsonObject node, string property) => Get(node, property) is JsonArray array ? array.Count : 0;
        var initialValues = Get(simulation, "initialValuesByPointId") is JsonObject values ? values.Count : 0;
        var bindingCount = Get(bindings, "bindings") is JsonObject bindingValues ? bindingValues.Count : 0;
        return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["devices"] = Count(device, "devices"),
            ["channels"] = Count(device, "channels"),
            ["points"] = Count(points, "points"),
            ["groups"] = Count(points, "groups"),
            ["initialValues"] = initialValues,
            ["changeRules"] = Count(simulation, "changeRules"),
            ["faultInjectionScenarios"] = Count(simulation, "faultInjectionScenarios"),
            ["signalBindings"] = bindingCount
        };
    }

    private static string HashSnapshot(DeviceConfigurationSnapshot snapshot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, content) in SerializeSnapshot(snapshot).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(content);
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static IReadOnlyDictionary<string, byte[]> SerializeSnapshot(DeviceConfigurationSnapshot snapshot)
    {
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["device.json"] = JsonSerializer.SerializeToUtf8Bytes(snapshot.Device, CurrentJson),
            ["points.json"] = JsonSerializer.SerializeToUtf8Bytes(snapshot.Points, CurrentJson),
            ["simulation.json"] = JsonSerializer.SerializeToUtf8Bytes(snapshot.Simulation, CurrentJson),
            ["signal-bindings.json"] = JsonSerializer.SerializeToUtf8Bytes(snapshot.SignalBindings, CurrentJson)
        };
        return bytes;
    }

    private static void NormalizeDictionaryOrder(DeviceConfigurationSnapshot snapshot)
    {
        snapshot.Simulation.InitialValuesByPointId = snapshot.Simulation.InitialValuesByPointId
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        snapshot.SignalBindings.Bindings = snapshot.SignalBindings.Bindings
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> MissingBindingsWarning(LegacyConfigurationInput input)
        => input.HasSignalBindings
            ? Array.Empty<string>()
            : new[] { "独立文件输入缺少 signal-bindings.json；按空绑定校验。执行器必需信号未配置时试验启动会被拒绝" };

    private static JsonObject ParseObjectNode(JsonNode? value, string path)
        => value is JsonObject result ? result : throw new UpgradeCommandException(2, $"{path} 必须为 JSON 对象");

    private static JsonObject RequireObject(JsonNode? value, string path) => ParseObjectNode(value, path);
    private static JsonArray RequireArray(JsonObject obj, string name, string path)
        => Get(obj, name) is JsonArray array ? array : throw new UpgradeCommandException(2, $"{path} 必须为 JSON 数组");
    private static IEnumerable<JsonNode?> OptionalArray(JsonObject obj, string name)
        => Get(obj, name) is JsonArray array ? array : Array.Empty<JsonNode?>();
    private static JsonObject? GetObject(JsonObject? obj, string name) => obj is null ? null : Get(obj, name) as JsonObject;
    private static JsonNode? Get(JsonObject? obj, string name)
    {
        if (obj is null) return null;
        var matches = obj.Where(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
            throw new UpgradeCommandException(2, $"JSON 对象 {name} 存在大小写重复字段");
        return matches.Length == 0 ? null : matches[0].Value;
    }
    private static bool Has(JsonObject obj, string name) => obj.Any(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
    private static void Set(JsonObject obj, string name, JsonNode? value)
    {
        Remove(obj, name);
        obj[name] = value;
    }
    private static bool Remove(JsonObject obj, string name, out JsonNode? value)
    {
        var key = obj.Select(pair => pair.Key).FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        if (key is null) { value = null; return false; }
        value = obj[key];
        return obj.Remove(key);
    }
    private static void Remove(JsonObject obj, string name) => Remove(obj, name, out _);

    private static string RequiredString(JsonObject obj, string name, string path)
        => OptionalString(obj, name)?.Trim() is { Length: > 0 } value
            ? value
            : throw new UpgradeCommandException(3, $"{path} 不能为空");
    private static string? OptionalString(JsonObject? obj, string name)
    {
        var node = Get(obj, name);
        if (node is null || node.GetValueKind() == System.Text.Json.JsonValueKind.Null) return null;
        return NodeString(node, name);
    }
    private static string NodeString(JsonNode node, string path)
    {
        try { return node.GetValue<string>() ?? string.Empty; }
        catch (InvalidOperationException) { throw new UpgradeCommandException(2, $"{path} 必须是字符串"); }
    }
    private static bool EnumTextEquals(JsonNode left, JsonNode right)
        => left.GetValueKind() == System.Text.Json.JsonValueKind.String
           && right.GetValueKind() == System.Text.Json.JsonValueKind.String
           && string.Equals(left.GetValue<string>(), right.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    private static string NodeText(JsonNode? value) => value?.ToJsonString() ?? "null";
    private static bool TryBoolean(JsonNode? node, out bool value)
    {
        try { value = node?.GetValue<bool>() ?? false; return node is not null; }
        catch (InvalidOperationException) { value = false; return false; }
    }
    private static bool TryInt32(JsonNode? node, out int value)
    {
        try { value = node?.GetValue<int>() ?? 0; return node is not null; }
        catch (InvalidOperationException) { value = 0; return false; }
        catch (FormatException) { value = 0; return false; }
    }
    private static int? GetOptionalInt(JsonObject? obj, string property)
    {
        var node = Get(obj, property);
        return node is null ? null : TryInt32(node, out var value) ? value : throw new UpgradeCommandException(3, $"{property} 必须是整数");
    }
    private static int? ReadOptionalPositiveInt(JsonNode? node, string path)
    {
        if (node is null) return null;
        if (!TryInt32(node, out var value) || value < 0)
            throw new UpgradeCommandException(3, $"{path} 是非法显式值");
        return value;
    }
    private static int? ReadRequiredPositiveIntIfPresent(JsonNode? node, string path)
    {
        if (node is null) return null;
        if (!TryInt32(node, out var value) || value <= 0)
            throw new UpgradeCommandException(3, $"{path} 是非法显式值，必须大于 0");
        return value;
    }
    private static int? ReadOptionalNonNegativeInt(JsonNode? node, string path)
    {
        if (node is null) return null;
        if (!TryInt32(node, out var value) || value < 0)
            throw new UpgradeCommandException(3, $"{path} 是非法显式值，不能小于 0");
        return value;
    }
    private static bool TryDecimal(JsonNode? node, out decimal value)
    {
        try { value = node?.GetValue<decimal>() ?? 0; return node is not null; }
        catch (InvalidOperationException) { value = 0; return false; }
        catch (FormatException) { value = 0; return false; }
    }
}
