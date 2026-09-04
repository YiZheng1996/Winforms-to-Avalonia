using System.Text.Json;

namespace XXX.TestBench.Core.Domain.TestPoints;

/// <summary>
/// 试验记录中固化的试验项点序列项，包含 ID、名称与执行器代码。
/// </summary>
public sealed record SequenceItem(
    int PointId,
    string Name,
    string ExecutorCode,
    string ResultKind,
    int SortOrder);

/// <summary>
/// 项点序列快照 JSON 编解码。启动时固化，执行与追溯只读快照，不依赖界面或后续配置变更。
/// </summary>
public static class SequenceSnapshot
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string ToJson(IReadOnlyList<SequenceItem> items) => JsonSerializer.Serialize(items, Options);

    public static IReadOnlyList<SequenceItem>? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<SequenceItem>>(json, Options); }
        catch (JsonException) { return null; }
    }
}
