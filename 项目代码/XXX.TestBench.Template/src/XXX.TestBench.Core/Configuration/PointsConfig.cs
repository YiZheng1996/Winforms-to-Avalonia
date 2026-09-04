using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 点位配置文件结构。
/// </summary>
public sealed class PointsConfig
{
    /// <summary>
    /// 配置结构版本号，必须与程序支持的版本一致。
    /// </summary>
    public int SchemaVersion { get; set; }
    /// <summary>
    /// 点位列表，配置中至少需要一个点位。
    /// </summary>
    public List<PointEntry> Points { get; set; } = new();

    /// <summary>
    /// 当前支持的配置版本号。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 单个点位定义。
    /// </summary>
    public sealed class PointEntry
    {
        /// <summary>
        /// 点位编码，全局唯一。
        /// </summary>
        public required string Code { get; set; }
        /// <summary>
        /// 点位名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 通信协议。
        /// </summary>
        public required string Protocol { get; set; }
        /// <summary>
        /// 通信地址。
        /// </summary>
        public required string Address { get; set; }
        /// <summary>
        /// 数据类型。
        /// </summary>
        public required string DataType { get; set; }
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
        public DevicePoint ToDomain() => new(Code, Protocol, Address, DataType, Unit, IsWritable, RiskLevel,
            EffectiveRawMin, EffectiveRawMax, EffectiveEngMin, EffectiveEngMax, Name, IsEnabled, Description);
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
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"points.json schemaVersion={SchemaVersion} 不受支持（期望 {CurrentSchemaVersion}）");

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
            if (string.IsNullOrWhiteSpace(point.Address))
                throw new ConfigValidationException($"点位 {point.Code} 的地址不能为空");
            if (string.IsNullOrWhiteSpace(point.DataType))
                throw new ConfigValidationException($"点位 {point.Code} 的数据类型不能为空");

            var scale = new[] { point.EffectiveRawMin, point.EffectiveRawMax, point.EffectiveEngMin, point.EffectiveEngMax };
            // 量程必须整组填写，不允许只填一半。
            if (scale.Any(value => value.HasValue) && scale.Any(value => !value.HasValue))
                throw new ConfigValidationException($"点位 {point.Code} 的量程必须完整填写原始/工程上下限");
            // 量程下限不能大于上限。
            if (point.EffectiveRawMin > point.EffectiveRawMax || point.EffectiveEngMin > point.EffectiveEngMax)
                throw new ConfigValidationException($"点位 {point.Code} 的量程下限不能大于上限");
        }
    }
}
