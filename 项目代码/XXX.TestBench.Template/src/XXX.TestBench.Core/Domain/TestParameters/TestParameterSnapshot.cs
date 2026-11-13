using System.Text.Json;
using System.Text.Json.Serialization;

namespace XXX.TestBench.Core.Domain.TestParameters;

/// <summary>
/// 试验记录参数快照的 JSON 编解码。启动时固化，执行与追溯只读快照，不依赖界面输入框。
/// 仅接受带 SchemaVersion 的当前合同，不再读取旧版无版本 JSON。
/// </summary>
public static class TestParameterSnapshot
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    /// <summary>
    /// 把有效参数序列化为版本化快照文本。
    /// </summary>
    public static string ToJson(EffectiveTestParameters value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(ParameterSnapshotV1.From(value), Options);
    }

    /// <summary>
    /// 把当前版本快照还原为有效参数；缺少版本、损坏或未知版本返回 null。
    /// </summary>
    public static EffectiveTestParameters? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<ParameterSnapshotV1>(json, Options);
            return snapshot?.SchemaVersion == CurrentSchemaVersion ? snapshot.ToDomain() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ParameterSnapshotV1
    {
        [JsonRequired, JsonPropertyOrder(0)] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
        [JsonPropertyOrder(1)] public required int ProductTypeId { get; init; }
        [JsonPropertyOrder(2)] public required int ProductModelId { get; init; }
        [JsonPropertyOrder(3)] public required int TestTimeSeconds { get; init; }
        [JsonPropertyOrder(4)] public required double TestVoltageV { get; init; }
        [JsonPropertyOrder(5)] public required double ProtectCurrentMa { get; init; }
        [JsonPropertyOrder(6)] public long ProjectRowVersion { get; init; }
        [JsonPropertyOrder(7)] public long ProductRowVersion { get; init; }

        public static ParameterSnapshotV1 From(EffectiveTestParameters value) => new()
        {
            ProductTypeId = value.ProductTypeId,
            ProductModelId = value.ProductModelId,
            TestTimeSeconds = value.TestTimeSeconds,
            TestVoltageV = value.TestVoltageV,
            ProtectCurrentMa = value.ProtectCurrentMa,
            ProjectRowVersion = value.ProjectRowVersion,
            ProductRowVersion = value.ProductRowVersion
        };

        public EffectiveTestParameters ToDomain() => new()
        {
            ProductTypeId = ProductTypeId,
            ProductModelId = ProductModelId,
            TestTimeSeconds = TestTimeSeconds,
            TestVoltageV = TestVoltageV,
            ProtectCurrentMa = ProtectCurrentMa,
            ProjectRowVersion = ProjectRowVersion,
            ProductRowVersion = ProductRowVersion
        };
    }
}
