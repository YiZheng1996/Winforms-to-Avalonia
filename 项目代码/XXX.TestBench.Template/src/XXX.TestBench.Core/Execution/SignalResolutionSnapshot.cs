using System.Text.Json;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 试验记录内固化的 SignalKey → PointId 解析结果。
/// </summary>
public sealed class SignalResolutionSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Revision { get; set; } = string.Empty;
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string ToJson(
        string revision,
        IReadOnlyDictionary<string, ResolvedSignal> signals)
        => JsonSerializer.Serialize(new SignalResolutionSnapshot
        {
            Revision = revision,
            Bindings = signals.ToDictionary(pair => pair.Key, pair => pair.Value.PointId, StringComparer.OrdinalIgnoreCase)
        }, Options);

    public static SignalResolutionSnapshot? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<SignalResolutionSnapshot>(json, Options);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
