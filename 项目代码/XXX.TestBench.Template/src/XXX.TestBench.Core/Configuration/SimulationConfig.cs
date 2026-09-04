using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 模拟运行配置信息。
/// </summary>
public sealed class SimulationConfig
{
    /// <summary>
    /// 配置结构版本号，必须与程序支持的版本一致。
    /// </summary>
    public int SchemaVersion { get; set; }
    /// <summary>
    /// 各点位的模拟初始值。
    /// </summary>
    public Dictionary<string, object?> InitialValues { get; set; } = new();
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
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 点位自动变化规则。
    /// </summary>
    public sealed class ChangeRule
    {
        /// <summary>
        /// 作用点位的通信地址。
        /// </summary>
        public required string Address { get; set; }
        /// <summary>
        /// 数值变化形式。
        /// </summary>
        public required string Pattern { get; set; }
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
        public required string Name { get; set; }
        /// <summary>
        /// 目标点位地址。
        /// </summary>
        public required string Address { get; set; }
        /// <summary>
        /// 故障行为描述。
        /// </summary>
        public required string Behavior { get; set; }
    }

    /// <summary>
    /// 校验模拟配置的版本号。
    /// </summary>
    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"simulation.json schemaVersion={SchemaVersion} 不受支持（期望 {CurrentSchemaVersion}）");
    }
}
