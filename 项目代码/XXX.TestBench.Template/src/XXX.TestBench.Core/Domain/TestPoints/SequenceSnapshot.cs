using System.Text.Json;
using System.Text.Json.Serialization;

namespace XXX.TestBench.Core.Domain.TestPoints;

/// <summary>
/// 项点序列快照 JSON 编解码。启动时固化，执行与追溯只读快照，不依赖界面或后续配置变更。
/// </summary>
public static class SequenceSnapshot
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string ToJson(IReadOnlyList<SequenceItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!IsValid(items)) throw new ArgumentException("项点序列包含无效身份、重复项点或无效合同版本", nameof(items));
        return JsonSerializer.Serialize(new Snapshot { SchemaVersion = CurrentSchemaVersion, Items = items }, Options);
    }

    public static IReadOnlyList<SequenceItem>? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(json, Options);
            return snapshot is { SchemaVersion: CurrentSchemaVersion, Items: not null } && IsValid(snapshot.Items)
                ? snapshot.Items : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsValid(IReadOnlyList<SequenceItem> items)
        => items.All(item => item is not null && item.PointId > 0
            && !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.ExecutorCode)
            && item.PointRowVersion >= 0 && item.OutputContractVersion > 0)
           && items.Select(item => item.PointId).Distinct().Count() == items.Count;

    private sealed record Snapshot
    {
        public required int SchemaVersion { get; init; }
        public required IReadOnlyList<SequenceItem> Items { get; init; }
    }
}
