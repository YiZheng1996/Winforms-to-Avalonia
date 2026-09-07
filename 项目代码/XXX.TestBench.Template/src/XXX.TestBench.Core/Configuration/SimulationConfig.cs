using XXX.TestBench.Core.Common;
using System.Text.Json.Serialization;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 仿真数值变化方式。
/// </summary>
public enum SimulationChangePattern
{
    Unknown = 0,
    Ramp = 1,
    Sine = 2
}

/// <summary>
/// 仿真故障行为。
/// </summary>
public enum SimulationFaultBehavior
{
    Unknown = 0,
    Stale = 1
}

/// <summary>
/// 仿真配置中的固定选项解析入口。
/// </summary>
public static class SimulationTypeCatalog
{
    public static bool TryParsePattern(string? value, out SimulationChangePattern pattern)
    {
        switch (Normalize(value))
        {
            case "ramp":
            case "线性":
            case "线性变化":
                pattern = SimulationChangePattern.Ramp;
                return true;
            case "sine":
            case "正弦":
            case "正弦变化":
                pattern = SimulationChangePattern.Sine;
                return true;
            default:
                pattern = SimulationChangePattern.Unknown;
                return false;
        }
    }

    public static bool TryParseBehavior(string? value, out SimulationFaultBehavior behavior)
    {
        switch (Normalize(value))
        {
            case "stale":
            case "陈旧":
            case "质量陈旧":
                behavior = SimulationFaultBehavior.Stale;
                return true;
            default:
                behavior = SimulationFaultBehavior.Unknown;
                return false;
        }
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}

/// <summary>
/// 模拟运行配置信息。
/// </summary>
public sealed class SimulationConfig
{
    /// <summary>
    /// 配置结构版本号，必须与程序支持的版本一致。
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>
    /// 各点位的模拟初始值。
    /// </summary>
    public Dictionary<string, object?> InitialValues { get; set; } = new();
    /// <summary>
    /// v2 按稳定 PointId 保存的初值；旧地址字典仅用于显式迁移。
    /// </summary>
    public Dictionary<string, object?> InitialValuesByPointId { get; set; } = new();
    /// <summary>
    /// 点位自动变化规则列表。
    /// </summary>
    public List<ChangeRule> ChangeRules { get; set; } = new();
    /// <summary>
    /// 故障注入场景列表。
    /// </summary>
    public List<FaultInjection> FaultInjectionScenarios { get; set; } = new();

    /// <summary>
    /// 当前支持的配置版本号。
    /// </summary>
    public const int LegacySchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// 点位自动变化规则。
    /// </summary>
    public sealed class ChangeRule
    {
        /// <summary>
        /// 作用点位的通信地址。
        /// </summary>
        public string Address { get; set; } = string.Empty;
        /// <summary>
        /// v2 作用点位身份。
        /// </summary>
        public string PointId { get; set; } = string.Empty;
        /// <summary>
        /// 数值变化形式。
        /// </summary>
        public string Pattern { get; set; } = string.Empty;
        /// <summary>
        /// 解析后的数值变化形式；Pattern 仅是配置兼容边界。
        /// </summary>
        [JsonIgnore]
        public SimulationChangePattern PatternKind
            => SimulationTypeCatalog.TryParsePattern(Pattern, out var value)
                ? value
                : SimulationChangePattern.Unknown;
        /// <summary>
        /// 每秒变化量。
        /// </summary>
        public double RatePerSecond { get; set; }
        /// <summary>
        /// 变化下限。
        /// </summary>
        public double Min { get; set; }
        /// <summary>
        /// 变化上限。
        /// </summary>
        public double Max { get; set; }
    }

    /// <summary>
    /// 故障注入场景。
    /// </summary>
    public sealed class FaultInjection
    {
        /// <summary>
        /// 场景名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 目标点位地址。
        /// </summary>
        public string Address { get; set; } = string.Empty;
        /// <summary>
        /// v2 作用点位身份。
        /// </summary>
        public string PointId { get; set; } = string.Empty;
        /// <summary>
        /// 故障行为描述。
        /// </summary>
        public string Behavior { get; set; } = string.Empty;
        /// <summary>
        /// 解析后的故障行为；Behavior 仅是配置兼容边界。
        /// </summary>
        [JsonIgnore]
        public SimulationFaultBehavior BehaviorKind
            => SimulationTypeCatalog.TryParseBehavior(Behavior, out var value)
                ? value
                : SimulationFaultBehavior.Unknown;
    }

    /// <summary>
    /// 校验模拟配置的版本号。
    /// </summary>
    public void Validate()
    {
        if (SchemaVersion == LegacySchemaVersion)
        {
            ValidateLegacy();
            return;
        }
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"simulation.json schemaVersion={SchemaVersion} 不受支持（支持 {LegacySchemaVersion} 迁移或当前版本 {CurrentSchemaVersion}）");

        ValidateV2();
    }

    private void ValidateLegacy()
    {

        foreach (var rule in ChangeRules ?? new List<ChangeRule>())
        {
            if (string.IsNullOrWhiteSpace(rule.Address))
                throw new ConfigValidationException("simulation.json 变化规则地址不能为空");
            if (rule.PatternKind == SimulationChangePattern.Unknown)
                throw new ConfigValidationException($"simulation.json 变化方式不受支持：{rule.Pattern}");
        }

        foreach (var scenario in FaultInjectionScenarios ?? new List<FaultInjection>())
        {
            if (string.IsNullOrWhiteSpace(scenario.Address))
                throw new ConfigValidationException("simulation.json 故障注入地址不能为空");
            if (scenario.BehaviorKind == SimulationFaultBehavior.Unknown)
                throw new ConfigValidationException($"simulation.json 故障行为不受支持：{scenario.Behavior}");
        }
    }

    private void ValidateV2()
    {
        foreach (var pair in InitialValuesByPointId ?? new Dictionary<string, object?>())
            if (!Guid.TryParse(pair.Key, out _))
                throw new ConfigValidationException($"simulation.json 初值 PointId 无效：{pair.Key}");

        foreach (var rule in ChangeRules ?? new List<ChangeRule>())
        {
            if (!Guid.TryParse(rule.PointId, out _))
                throw new ConfigValidationException($"simulation.json 变化规则 PointId 无效：{rule.PointId}");
            if (rule.PatternKind == SimulationChangePattern.Unknown)
                throw new ConfigValidationException($"simulation.json 变化方式不受支持：{rule.Pattern}");
            if (rule.Min > rule.Max)
                throw new ConfigValidationException("simulation.json 变化规则下限不能大于上限");
        }

        foreach (var scenario in FaultInjectionScenarios ?? new List<FaultInjection>())
        {
            if (!Guid.TryParse(scenario.PointId, out _))
                throw new ConfigValidationException($"simulation.json 故障注入 PointId 无效：{scenario.PointId}");
            if (scenario.BehaviorKind == SimulationFaultBehavior.Unknown)
                throw new ConfigValidationException($"simulation.json 故障行为不受支持：{scenario.Behavior}");
        }
    }
}
