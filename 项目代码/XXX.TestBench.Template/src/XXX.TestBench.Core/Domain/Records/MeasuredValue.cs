using XXX.TestBench.Core.Domain.Devices;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 单个项点产生的结构化测量。Number/Text/Boolean 中只能有与 Kind 对应的一项。
/// </summary>
public sealed record MeasuredValue(
    [property: JsonPropertyOrder(0)] string Key,
    [property: JsonPropertyOrder(1)] MeasuredValueKind Kind,
    [property: JsonPropertyOrder(2)] double? Number,
    [property: JsonPropertyOrder(3)] string? Text,
    [property: JsonPropertyOrder(4)] bool? Boolean,
    [property: JsonPropertyOrder(5)] string? Unit,
    [property: JsonPropertyOrder(6)] DateTime CapturedAtUtc,
    [property: JsonPropertyOrder(7)] string? SourcePointId,
    [property: JsonPropertyOrder(8)] string? DeviceId,
    [property: JsonPropertyOrder(9)] string? Revision,
    [property: JsonPropertyOrder(10)] long? ConnectionGeneration,
    [property: JsonPropertyOrder(11)] PointQuality? SourceQuality,
    [property: JsonPropertyOrder(12)] bool Valid,
    [property: JsonPropertyOrder(13)] string? Error)
{
    /// <summary>
    /// 检查值类型、有限数值和可选设备来源是否一致。
    /// </summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Key))
            throw new ArgumentException("测量键不能为空", nameof(Key));
        if (!Enum.IsDefined(Kind))
            throw new ArgumentException($"未知测量类型：{Kind}", nameof(Kind));
        if (CapturedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("测量时间必须是 UTC", nameof(CapturedAtUtc));

        var valueCount = (Number.HasValue ? 1 : 0) + (Text is not null ? 1 : 0) + (Boolean.HasValue ? 1 : 0);
        var kindMatchesValue = Kind switch
        {
            MeasuredValueKind.Number => Text is null && !Boolean.HasValue,
            MeasuredValueKind.Text => Number is null && !Boolean.HasValue,
            MeasuredValueKind.Boolean => Number is null && Text is null,
            _ => false
        };

        if (!kindMatchesValue || valueCount > 1 || (Valid && valueCount != 1))
            throw new ArgumentException("测量值与 Kind 不一致；有效测量必须提供且只能提供一种值类型");
        if (Number is { } number && !double.IsFinite(number))
            throw new ArgumentException("数值测量必须是有限数", nameof(Number));

        var hasAnyDeviceSource = SourcePointId is not null
            || DeviceId is not null
            || Revision is not null
            || ConnectionGeneration.HasValue
            || SourceQuality.HasValue;
        var hasCompleteDeviceSource = !string.IsNullOrWhiteSpace(SourcePointId)
            && !string.IsNullOrWhiteSpace(DeviceId)
            && !string.IsNullOrWhiteSpace(Revision)
            && ConnectionGeneration.HasValue
            && SourceQuality.HasValue;
        if (hasAnyDeviceSource && !hasCompleteDeviceSource)
            throw new ArgumentException("设备来源信息必须同时包含点位、设备、配置版本、连接代次和质量");
        if (SourceQuality.HasValue && !Enum.IsDefined(SourceQuality.Value))
            throw new ArgumentException($"未知设备质量：{SourceQuality}", nameof(SourceQuality));
        if (!Valid && string.IsNullOrWhiteSpace(Error))
            throw new ArgumentException("无效测量必须说明原因", nameof(Error));
    }
}

/// <summary>
/// 测量值的基础类型。
/// </summary>
public enum MeasuredValueKind
{
    Number = 0,
    Text = 1,
    Boolean = 2
}

/// <summary>
/// 单个项点的结构化测量快照；未知版本禁止用于报告或产品判定。
/// </summary>
public sealed record MeasuredValuesSnapshotV1
{
    [JsonRequired, JsonPropertyOrder(0)] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyOrder(1)] public required IReadOnlyList<MeasuredValue> Values { get; init; }
}

public sealed record SerializedMeasuredValues(string JsonText, string Sha256)
{
    public byte[] GetUtf8Bytes() => new UTF8Encoding(false).GetBytes(JsonText);
}

/// <summary>
/// 固定 UTF-8 JSON 序列化入口，字段顺序固定，时间按 UTC O 格式保存。
/// </summary>
public static class MeasuredValuesSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static SerializedMeasuredValues Serialize(IReadOnlyList<MeasuredValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Validate(values);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new MeasuredValuesSnapshotV1 { Values = values }, Options);
        return new SerializedMeasuredValues(
            Encoding.UTF8.GetString(bytes),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public static IReadOnlyList<MeasuredValue> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json[0] == '\uFEFF') throw new InvalidDataException("测量 JSON 必须是无 BOM 文本");
        var snapshot = JsonSerializer.Deserialize<MeasuredValuesSnapshotV1>(json, Options)
            ?? throw new InvalidDataException("测量 JSON 为空");
        if (snapshot.SchemaVersion != 1)
            throw new InvalidDataException($"不支持的测量 JSON 版本：{snapshot.SchemaVersion}");
        var values = snapshot.Values ?? throw new InvalidDataException("测量列表为空");
        Validate(values);
        return values;
    }

    public static string ComputeHash(string originalJson)
    {
        ArgumentNullException.ThrowIfNull(originalJson);
        return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(originalJson))).ToLowerInvariant();
    }

    private static void Validate(IReadOnlyList<MeasuredValue> values)
    {
        if (values.Any(value => value is null))
            throw new InvalidDataException("测量列表不能包含空项");
        var duplicate = values
            .GroupBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"测量键重复或仅大小写不同：{duplicate.Key}");
        foreach (var value in values)
            value.EnsureValid();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };
        options.Converters.Add(new UtcRoundTripDateTimeConverter());
        return options;
    }

    private sealed class UtcRoundTripDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (!DateTime.TryParseExact(text, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var value)
                || value.Kind != DateTimeKind.Utc)
                throw new JsonException("采样时间必须是 UTC O 格式");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            if (value.Kind != DateTimeKind.Utc) throw new JsonException("采样时间必须是 UTC");
            writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
        }
    }
}
