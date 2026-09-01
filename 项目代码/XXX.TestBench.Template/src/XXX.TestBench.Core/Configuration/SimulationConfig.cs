using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

public sealed class SimulationConfig
{
    public int SchemaVersion { get; set; }
    public Dictionary<string, object?> InitialValues { get; set; } = new();
    public List<ChangeRule> ChangeRules { get; set; } = new();
    public List<FaultInjection> FaultInjectionScenarios { get; set; } = new();

    public const int CurrentSchemaVersion = 1;

    public sealed class ChangeRule
    {
        public required string Address { get; set; }
        public required string Pattern { get; set; }
        public double RatePerSecond { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
    }

    public sealed class FaultInjection
    {
        public required string Name { get; set; }
        public required string Address { get; set; }
        public required string Behavior { get; set; }
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"simulation.json schemaVersion={SchemaVersion} 不受支持（期望 {CurrentSchemaVersion}）");
    }
}
