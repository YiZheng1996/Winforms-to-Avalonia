using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using System.Text.Json.Serialization;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 点位配置文件结构。
/// </summary>
public sealed class PointsConfig
{
    /// <summary>
    /// 配置结构版本号，必须与程序支持的版本一致。
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>
    /// 点位列表，配置中至少需要一个点位。
    /// </summary>
    public List<PointEntry> Points { get; set; } = new();
    /// <summary>
    /// 点位分组列表。分组只用于树状界面和筛选，不参与驱动寻址或运行时路由。
    /// </summary>
    public List<PointGroupEntry> Groups { get; set; } = new();

    /// <summary>
    /// 当前支持的配置版本号。
    /// </summary>
    public const int LegacySchemaVersion = 1;
    public const int PreviousSchemaVersion = 2;
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// 单个点位定义。
    /// </summary>
    public sealed class PointEntry
    {
        /// <summary>
        /// 持久化点位身份，改名称不改变该值。
        /// </summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>
        /// 点位编码，全局唯一。
        /// </summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>
        /// 点位名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 通信协议。
        /// </summary>
        public string Protocol { get; set; } = string.Empty;
        /// <summary>
        /// 所属设备身份；点位不再独立决定真实驱动。
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;
        /// <summary>
        /// 导入边界中的设备编码。解析完成后由设备配置服务解析为 DeviceId，不落盘。
        /// </summary>
        [JsonIgnore]
        public string DeviceCode { get; set; } = string.Empty;
        /// <summary>
        /// 所属点位分组身份。分组仅用于组织和筛选，不改变地址唯一性或运行时路由。
        /// </summary>
        public string GroupId { get; set; } = string.Empty;
        /// <summary>
        /// 导入边界中的分组编码，解析完成后由页面映射为 GroupId，不落盘。
        /// </summary>
        [JsonIgnore]
        public string GroupCode { get; set; } = string.Empty;
        /// <summary>
        /// 解析后的通信方式。Protocol 字符串只保留在 JSON 兼容边界。
        /// </summary>
        [JsonIgnore]
        public DevicePointProtocol ProtocolKind
            => DevicePointTypeCatalog.TryParseProtocol(Protocol, out var value)
                ? value
                : DevicePointProtocol.Unknown;
        /// <summary>
        /// 通信地址。
        /// </summary>
        public string Address { get; set; } = string.Empty;
        /// <summary>
        /// 驱动专用的结构化地址参数；Address 保留为可读/兼容表示。
        /// </summary>
        public PointAddressDefinition? AddressDefinition { get; set; }
        /// <summary>
        /// 数据类型。
        /// </summary>
        public string DataType { get; set; } = string.Empty;
        /// <summary>
        /// v2 原始数据类型。旧 Decimal 只作为兼容类型保留，不猜测为 Float32。
        /// </summary>
        public string RawDataType { get; set; } = string.Empty;
        /// <summary>
        /// 解析后的数据类型。DataType 字符串只保留在 JSON 兼容边界。
        /// </summary>
        [JsonIgnore]
        public DevicePointDataType DataTypeKind
            => DevicePointTypeCatalog.TryParseDataType(DataType, out var value)
                ? value
                : DevicePointDataType.Unknown;
        /// <summary>
        /// v2 实际采用的数据类型；未填写时回退到旧 DataType。
        /// </summary>
        [JsonIgnore]
        public DevicePointDataType RawDataTypeKind
            => DevicePointTypeCatalog.TryParseDataType(
                    string.IsNullOrWhiteSpace(RawDataType) ? DataType : RawDataType,
                    out var value)
                ? value
                : DevicePointDataType.Unknown;
        /// <summary>
        /// 解码字节序和字序。
        /// </summary>
        public DecodeOptions DecodeOptions { get; set; } = new();
        /// <summary>
        /// 第一版可执行写入策略，仅支持写后新鲜回读相等。
        /// </summary>
        public PointWritePolicy WritePolicy { get; set; } = PointWritePolicy.ReadBackEqual;
        /// <summary>
        /// 工程单位。
        /// </summary>
        public string Unit { get; set; } = string.Empty;
        /// <summary>
        /// 是否允许写入。
        /// </summary>
        public bool IsWritable { get; set; }
        /// <summary>
        /// 是否启用该点位。
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        /// <summary>
        /// 写入风险等级。
        /// </summary>
        public WriteRiskLevel RiskLevel { get; set; } = WriteRiskLevel.Normal;

        /// <summary>
        /// 旧格式的量程对象；新配置仍支持扁平字段写法。
        /// </summary>
        public ScaleValues? Scale { get; set; }
        /// <summary>
        /// 原始值下限。
        /// </summary>
        public decimal? RawMin { get; set; }
        /// <summary>
        /// 原始值上限。
        /// </summary>
        public decimal? RawMax { get; set; }
        /// <summary>
        /// 工程值下限。
        /// </summary>
        public decimal? EngMin { get; set; }
        /// <summary>
        /// 工程值上限。
        /// </summary>
        public decimal? EngMax { get; set; }
        /// <summary>
        /// 点位用途说明。
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 实际采用的原始值下限，兼容旧量程写法。
        /// </summary>
        public decimal? EffectiveRawMin => RawMin ?? Scale?.RawMin;
        /// <summary>
        /// 实际采用的原始值上限，兼容旧量程写法。
        /// </summary>
        public decimal? EffectiveRawMax => RawMax ?? Scale?.RawMax;
        /// <summary>
        /// 实际采用的工程值下限，兼容旧量程写法。
        /// </summary>
        public decimal? EffectiveEngMin => EngMin ?? Scale?.EngMin;
        /// <summary>
        /// 实际采用的工程值上限，兼容旧量程写法。
        /// </summary>
        public decimal? EffectiveEngMax => EngMax ?? Scale?.EngMax;

        /// <summary>
        /// 把配置点位转换为领域对象。
        /// </summary>
        public DevicePoint ToDomain(string? driverKey = null, string? revision = null)
        {
            var protocol = ProtocolKind;
            if (protocol == DevicePointProtocol.Unknown)
                DevicePointTypeCatalog.TryParseDriverKey(driverKey, out protocol);
            if (protocol == DevicePointProtocol.Unknown)
                throw new ConfigValidationException($"点位 {Code} 的通信方式不受支持：{Protocol}");
            if (RawDataTypeKind == DevicePointDataType.Unknown)
                throw new ConfigValidationException($"点位 {Code} 的数据类型不受支持：{DataType}");

            return new DevicePoint(Code, protocol, Address, RawDataTypeKind, Unit, IsWritable, RiskLevel,
                EffectiveRawMin, EffectiveRawMax, EffectiveEngMin, EffectiveEngMax, Name, IsEnabled, Description,
                Id, DeviceId, string.Empty, AddressDefinition, DecodeOptions, WritePolicy, revision ?? string.Empty);
        }
    }

    /// <summary>
    /// 单层点位分组定义。分组必须归属于一个设备，但不持有地址、驱动或实时值。
    /// </summary>
    public sealed class PointGroupEntry
    {
        /// <summary>
        /// 持久化分组身份，修改名称或排序不改变该值。
        /// </summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>
        /// 所属设备身份。
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;
        /// <summary>
        /// 设备内唯一分组编码。
        /// </summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>
        /// 分组显示名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 分组说明。
        /// </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>
        /// 同一设备内的显示顺序。
        /// </summary>
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// 量程上下限集合，供旧配置文件兼容使用。
    /// </summary>
    public sealed class ScaleValues
    {
        /// <summary>
        /// 原始值下限。
        /// </summary>
        public decimal? RawMin { get; set; }
        /// <summary>
        /// 原始值上限。
        /// </summary>
        public decimal? RawMax { get; set; }
        /// <summary>
        /// 工程值下限。
        /// </summary>
        public decimal? EngMin { get; set; }
        /// <summary>
        /// 工程值上限。
        /// </summary>
        public decimal? EngMax { get; set; }
    }

    /// <summary>
    /// 校验点位配置，存在空值或重复项时直接报错。
    /// </summary>
    public void Validate()
    {
        if (SchemaVersion == LegacySchemaVersion)
        {
            ValidateLegacy();
            return;
        }
        if (SchemaVersion != PreviousSchemaVersion && SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"points.json schemaVersion={SchemaVersion} 不受支持（支持 {LegacySchemaVersion}/{PreviousSchemaVersion} 迁移或当前版本 {CurrentSchemaVersion}）");

        if (SchemaVersion == PreviousSchemaVersion)
            ValidateV2();
        else
            ValidateV3();
    }

    private void ValidateLegacy()
    {

        // 至少需要一个点位，否则直接报错。
        if (Points is null || Points.Count == 0)
            throw new ConfigValidationException("points.json 至少需要配置一个点位");

        // 不允许存在空点位行。
        if (Points.Any(point => point is null))
            throw new ConfigValidationException("points.json 存在空点位行");

        // 收集点位编码，检查空值和重复。
        var codes = Points.Select(p => p.Code?.Trim() ?? string.Empty).ToList();
        if (codes.Any(string.IsNullOrWhiteSpace))
            throw new ConfigValidationException("点位编码不能为空");
        if (codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Count)
            throw new ConfigValidationException("points.json 存在重复点位代码");

        // 用协议和地址拼成连接键，检查空值和重复。
        var addresses = Points
            .Select(p => $"{p.Protocol?.Trim() ?? string.Empty}\u001f{p.Address?.Trim() ?? string.Empty}")
            .ToList();
        if (addresses.Any(key => key.StartsWith("\u001f", StringComparison.Ordinal) || key.EndsWith("\u001f", StringComparison.Ordinal)))
            throw new ConfigValidationException("点位协议和地址不能为空");
        if (addresses.Distinct(StringComparer.OrdinalIgnoreCase).Count() != addresses.Count)
            throw new ConfigValidationException("points.json 存在重复的协议/地址组合");

        foreach (var point in Points)
        {
            if (string.IsNullOrWhiteSpace(point.Protocol))
                throw new ConfigValidationException($"点位 {point.Code} 的协议不能为空");
            if (point.ProtocolKind == DevicePointProtocol.Unknown)
                throw new ConfigValidationException($"点位 {point.Code} 的通信方式不受支持：{point.Protocol}");
            if (string.IsNullOrWhiteSpace(point.Address))
                throw new ConfigValidationException($"点位 {point.Code} 的地址不能为空");
            if (string.IsNullOrWhiteSpace(point.DataType))
                throw new ConfigValidationException($"点位 {point.Code} 的数据类型不能为空");
            if (point.DataTypeKind == DevicePointDataType.Unknown)
                throw new ConfigValidationException($"点位 {point.Code} 的数据类型不受支持：{point.DataType}");
            // 只读点位没有写入动作，不能再配置高风险等级，避免导入或旧配置产生矛盾语义。
            if (!point.IsWritable && point.RiskLevel == WriteRiskLevel.HighRisk)
                throw new ConfigValidationException($"点位 {point.Code} 为只读点位，不能设置为高风险写入");

            var scale = new[] { point.EffectiveRawMin, point.EffectiveRawMax, point.EffectiveEngMin, point.EffectiveEngMax };
            // 量程必须整组填写，不允许只填一半。
            if (scale.Any(value => value.HasValue) && scale.Any(value => !value.HasValue))
                throw new ConfigValidationException($"点位 {point.Code} 的量程必须完整填写原始/工程上下限");
            // 量程下限不能大于上限。
            if (point.EffectiveRawMin > point.EffectiveRawMax || point.EffectiveEngMin > point.EffectiveEngMax)
                throw new ConfigValidationException($"点位 {point.Code} 的量程下限不能大于上限");
        }
    }

    private void ValidateV2()
    {
        if (Points is null || Points.Count == 0)
            throw new ConfigValidationException("points.json 至少需要配置一个点位");
        if (Points.Any(point => point is null))
            throw new ConfigValidationException("points.json 存在空点位行");

        var codes = Points.Select(point => point.Code?.Trim() ?? string.Empty).ToList();
        if (codes.Any(string.IsNullOrWhiteSpace))
            throw new ConfigValidationException("points.json 点位编码不能为空");
        if (codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Count)
            throw new ConfigValidationException("points.json 存在重复点位代码");

        var ids = Points.Select(point => point.Id).ToList();
        if (ids.Any(id => !Guid.TryParse(id, out _)))
            throw new ConfigValidationException("points.json 每个点位 Id 必须是有效 GUID");
        if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            throw new ConfigValidationException("points.json 存在重复点位 Id");

        var addressKeys = Points
            .Select(point => $"{point.DeviceId?.Trim() ?? string.Empty}\u001f{point.AddressDefinition?.ToCanonical(point.Address ?? string.Empty) ?? point.Address?.Trim() ?? string.Empty}")
            .ToList();
        if (addressKeys.Any(key => key.StartsWith("\u001f", StringComparison.Ordinal) || key.EndsWith("\u001f", StringComparison.Ordinal)))
            throw new ConfigValidationException("点位所属设备和地址不能为空");
        if (addressKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != addressKeys.Count)
            throw new ConfigValidationException("同一设备存在重复的规范化地址");

        foreach (var point in Points)
        {
            if (string.IsNullOrWhiteSpace(point.DeviceId))
                throw new ConfigValidationException($"点位 {point.Code} 的 DeviceId 不能为空");
            if (string.IsNullOrWhiteSpace(point.Address))
                throw new ConfigValidationException($"点位 {point.Code} 的地址不能为空");
            if (point.RawDataTypeKind == DevicePointDataType.Unknown)
                throw new ConfigValidationException($"点位 {point.Code} 的原始数据类型不受支持：{point.RawDataType}");
            if (!point.IsWritable && point.RiskLevel == WriteRiskLevel.HighRisk)
                throw new ConfigValidationException($"点位 {point.Code} 为只读点位，不能设置为高风险写入");
            if (point.IsWritable && point.WritePolicy != PointWritePolicy.ReadBackEqual)
                throw new ConfigValidationException($"点位 {point.Code} 的写入策略不受支持：{point.WritePolicy}");

            var scale = new[] { point.EffectiveRawMin, point.EffectiveRawMax, point.EffectiveEngMin, point.EffectiveEngMax };
            if (scale.Any(value => value.HasValue) && scale.Any(value => !value.HasValue))
                throw new ConfigValidationException($"点位 {point.Code} 的量程必须完整填写原始/工程上下限");
            if (point.EffectiveRawMin > point.EffectiveRawMax || point.EffectiveEngMin > point.EffectiveEngMax)
                throw new ConfigValidationException($"点位 {point.Code} 的量程下限不能大于上限");
            if ((point.EffectiveRawMin.HasValue && point.EffectiveRawMax.HasValue
                 && point.EffectiveRawMin == point.EffectiveRawMax)
                || (point.EffectiveEngMin.HasValue && point.EffectiveEngMax.HasValue
                    && point.EffectiveEngMin == point.EffectiveEngMax))
                throw new ConfigValidationException($"点位 {point.Code} 的量程上下限不能相等");
        }
    }

    private void ValidateV3()
    {
        ValidateV2();

        if (Groups is null || Groups.Count == 0)
            throw new ConfigValidationException("points.json 每个设备至少需要一个点位分组");
        if (Groups.Any(group => group is null))
            throw new ConfigValidationException("points.json 存在空点位分组");

        var groupIds = Groups.Select(group => group.Id?.Trim() ?? string.Empty).ToList();
        if (groupIds.Any(id => !Guid.TryParse(id, out _)))
            throw new ConfigValidationException("points.json 每个点位分组 Id 必须是有效 GUID");
        if (groupIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != groupIds.Count)
            throw new ConfigValidationException("points.json 存在重复点位分组 Id");

        var groupCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups)
        {
            if (string.IsNullOrWhiteSpace(group.DeviceId))
                throw new ConfigValidationException($"点位分组 {group.Code} 的 DeviceId 不能为空");
            if (string.IsNullOrWhiteSpace(group.Code))
                throw new ConfigValidationException("点位分组编码不能为空");
            if (string.IsNullOrWhiteSpace(group.Name))
                throw new ConfigValidationException($"点位分组 {group.Code} 的名称不能为空");

            var deviceGroupKey = $"{group.DeviceId.Trim()}\u001f{group.Code.Trim()}";
            if (!groupCodes.Add(deviceGroupKey))
                throw new ConfigValidationException($"同一设备存在重复点位分组编码：{group.Code}");
        }

        var groupsById = Groups.ToDictionary(group => group.Id.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var point in Points)
        {
            if (string.IsNullOrWhiteSpace(point.GroupId))
                throw new ConfigValidationException($"点位 {point.Code} 的 GroupId 不能为空");
            if (!groupsById.TryGetValue(point.GroupId.Trim(), out var group))
                throw new ConfigValidationException($"点位 {point.Code} 引用了不存在的点位分组：{point.GroupId}");
            if (!string.Equals(group.DeviceId.Trim(), point.DeviceId.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ConfigValidationException($"点位 {point.Code} 与点位分组 {group.Code} 不属于同一设备");
        }
    }
}
