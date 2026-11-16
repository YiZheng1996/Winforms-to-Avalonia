using System.Reflection;

namespace XXX.TestBench.Upgrade;

public sealed class UpgradeReport
{
    public string Tool { get; init; } = "XXX.TestBench.Upgrade";
    public string ToolVersion { get; init; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    public required string Command { get; set; }
    public string? SourcePath { get; set; }
    public string? OutputPath { get; set; }
    public string? SourceSha256 { get; set; }
    public string? OutputSha256 { get; set; }
    public string? SourceConfigurationVersions { get; set; }
    public string? OutputConfigurationVersions { get; set; }
    public int FailureCode { get; set; } = 1;
    public Dictionary<string, string> InputFileSha256 { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> OutputFileSha256 { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int? SourceVersion { get; set; }
    public int? OutputVersion { get; set; }
    public string? DatabaseInstanceId { get; set; }
    public string? StorageId { get; set; }
    public Dictionary<string, long> BeforeRowCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> AfterRowCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, string> LegacyRunIdMappings { get; } = new();
    public List<string> Changes { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Blockers { get; } = new();
    public bool OutputWritten { get; set; }
    public bool Success { get; set; }
}
