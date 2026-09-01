using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

public sealed class AppPaths
{
    public string Database { get; set; } = string.Empty;
    public string ReportOutput { get; set; } = string.Empty;
    public string ReportTemplates { get; set; } = string.Empty;
}

public sealed class AppConfig
{
    public int SchemaVersion { get; set; }
    public required string SystemName { get; set; }
    public required string Brand { get; set; }
    public required string Language { get; set; }
    public Dictionary<string, bool> Modules { get; set; } = new();
    public AppPaths DefaultPaths { get; set; } = new();

    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"app.json schemaVersion={SchemaVersion} 不受支持（期望 {CurrentSchemaVersion}）");
        if (string.IsNullOrWhiteSpace(SystemName)) throw new ConfigValidationException("app.json 缺少 systemName");
        if (string.IsNullOrWhiteSpace(DefaultPaths.Database)) throw new ConfigValidationException("app.json 缺少 defaultPaths.database");
    }
}
