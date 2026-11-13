using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>
/// 报表计算使用的冻结输入。列表顺序是输入合同的一部分，重试必须复用已保存的原始 JSON。
/// </summary>
public sealed record ReportInputSnapshotV1
{
    [JsonRequired, JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyOrder(1)]
    public required string DatabaseInstanceId { get; init; }

    [JsonPropertyOrder(2)]
    public required string RunId { get; init; }

    [JsonPropertyOrder(3)]
    public int ResultContractVersion { get; init; }

    [JsonPropertyOrder(4)]
    public ReportPurpose Purpose { get; init; }

    [JsonPropertyOrder(5)]
    public RunScope Scope { get; init; }

    [JsonPropertyOrder(6)]
    public RecordState ExecutionState { get; init; }

    [JsonPropertyOrder(7)]
    public string? ExecutionError { get; init; }

    [JsonPropertyOrder(8)]
    public required ReportInputMetadata Metadata { get; init; }

    [JsonPropertyOrder(9)]
    public required IReadOnlyList<ReportInputParameter> Parameters { get; init; }

    [JsonPropertyOrder(10)]
    public required IReadOnlyList<ReportInputTestItem> Items { get; init; }

    [JsonPropertyOrder(11)]
    public required IReadOnlyList<ReportInputDeviceMode> DeviceModes { get; init; }

    [JsonPropertyOrder(12)]
    public required ReportInputResources Resources { get; init; }

    [JsonPropertyOrder(13)]
    public required string TargetTimeZoneId { get; init; }
}

/// <summary>
/// 来自试验记录和运行快照的允许元数据字段。
/// </summary>
public sealed record ReportInputMetadata
{
    [JsonPropertyOrder(0)] public required string RecordNumber { get; init; }
    [JsonPropertyOrder(1)] public required string RunId { get; init; }
    [JsonPropertyOrder(2)] public string? ProductNumber { get; init; }
    [JsonPropertyOrder(3)] public string? VehicleNumber { get; init; }
    [JsonPropertyOrder(4)] public string? BatchNumber { get; init; }
    [JsonPropertyOrder(5)] public string? StationNumber { get; init; }
    [JsonPropertyOrder(6)] public string? Remark { get; init; }
    [JsonPropertyOrder(7)] public required string ProductModelName { get; init; }
    [JsonPropertyOrder(8)] public required string OperatorName { get; init; }
    [JsonPropertyOrder(9)] public DateTime StartedAtUtc { get; init; }
    [JsonPropertyOrder(10)] public DateTime? FinishedAtUtc { get; init; }
    [JsonPropertyOrder(11)] public DeviceMode DeviceMode { get; init; }
    [JsonPropertyOrder(12)] public RunScope Scope { get; init; }
    [JsonPropertyOrder(13)] public RecordState ExecutionState { get; init; }
}

/// <summary>
/// 冻结的实际设备模式；用于区分全仿真、硬件和混合运行时的来源。
/// </summary>
public sealed record ReportInputDeviceMode(string DeviceId, DeviceMode Mode);

/// <summary>
/// 一个固定参数字段的冻结值。
/// </summary>
public sealed record ReportInputParameter
{
    [JsonPropertyOrder(0)] public required string Field { get; init; }
    [JsonPropertyOrder(1)] public MeasuredValueKind Kind { get; init; }
    [JsonPropertyOrder(2)] public double? Number { get; init; }
    [JsonPropertyOrder(3)] public string? Text { get; init; }
    [JsonPropertyOrder(4)] public bool? Boolean { get; init; }
    [JsonPropertyOrder(5)] public string? Unit { get; init; }

    internal void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Field))
            throw new InvalidDataException("报表参数字段名不能为空");

        var count = (Number.HasValue ? 1 : 0) + (Text is not null ? 1 : 0) + (Boolean.HasValue ? 1 : 0);
        var matches = Kind switch
        {
            MeasuredValueKind.Number => Number.HasValue && Text is null && !Boolean.HasValue,
            MeasuredValueKind.Text => Number is null && Text is not null && !Boolean.HasValue,
            MeasuredValueKind.Boolean => Number is null && Text is null && Boolean.HasValue,
            _ => false
        };
        if (!matches || count != 1)
            throw new InvalidDataException($"报表参数 {Field} 的类型和值不一致");
        if (Number is { } number && !double.IsFinite(number))
            throw new InvalidDataException($"报表参数 {Field} 不是有限数");
    }
}

/// <summary>
/// 按开始时冻结顺序保存的一个试验项点及其测量。
/// </summary>
public sealed record ReportInputTestItem
{
    [JsonPropertyOrder(0)] public int PointId { get; init; }
    [JsonPropertyOrder(1)] public required string Name { get; init; }
    [JsonPropertyOrder(2)] public required string ExecutorCode { get; init; }
    [JsonPropertyOrder(3)] public int OutputContractVersion { get; init; }
    [JsonPropertyOrder(4)] public ItemResultState State { get; init; }
    [JsonPropertyOrder(5)] public required IReadOnlyList<MeasuredValue> Measurements { get; init; }
}

/// <summary>
/// 本次报告实际使用的不可变资源版本摘要。
/// </summary>
public sealed record ReportInputResources
{
    [JsonPropertyOrder(0)] public required string TemplateKey { get; init; }
    [JsonPropertyOrder(1)] public required string TemplateHash { get; init; }
    [JsonPropertyOrder(2)] public required string MappingKey { get; init; }
    [JsonPropertyOrder(3)] public required string MappingHash { get; init; }
    [JsonPropertyOrder(4)] public string? GuidanceKey { get; init; }
    [JsonPropertyOrder(5)] public string? GuidanceHash { get; init; }
}

/// <summary>
/// 已序列化的不可变报表输入文本及其原始 UTF-8 字节摘要。
/// </summary>
public sealed record SerializedReportInput(string JsonText, string InputHash)
{
    public byte[] GetUtf8Bytes() => new UTF8Encoding(false).GetBytes(JsonText);
}

/// <summary>
/// 生成报表输入 JSON 的固定序列化入口。哈希始终对其输出的 UTF-8 无 BOM 原始字节计算。
/// </summary>
public static class ReportInputSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static SerializedReportInput Serialize(ReportInputSnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new SerializedReportInput(Encoding.UTF8.GetString(bytes), hash);
    }

    public static ReportInputSnapshotV1 Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json[0] == '\uFEFF')
            throw new InvalidDataException("报表输入 JSON 必须是无 BOM 文本");
        var snapshot = JsonSerializer.Deserialize<ReportInputSnapshotV1>(json, Options)
            ?? throw new InvalidDataException("报表输入 JSON 为空");
        Validate(snapshot);
        return snapshot;
    }

    public static string ComputeHash(string originalJson)
    {
        ArgumentNullException.ThrowIfNull(originalJson);
        var bytes = new UTF8Encoding(false).GetBytes(originalJson);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static void Validate(ReportInputSnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1)
            throw new InvalidDataException($"不支持的报表输入版本：{snapshot.SchemaVersion}");
        RequireGuid(snapshot.DatabaseInstanceId, nameof(snapshot.DatabaseInstanceId));
        RequireGuid(snapshot.RunId, nameof(snapshot.RunId));
        if (snapshot.ResultContractVersion != 2)
            throw new InvalidDataException($"仅允许结果合同版本 2 生成新报表输入，实际为 {snapshot.ResultContractVersion}");
        if (!Enum.IsDefined(snapshot.Purpose) || !Enum.IsDefined(snapshot.Scope)
            || !Enum.IsDefined(snapshot.ExecutionState))
            throw new InvalidDataException("报表输入包含未知用途、范围或执行状态");
        if (snapshot.ExecutionState == RecordState.Running)
            throw new InvalidDataException("运行中的记录不能生成终态报表输入");
        if (snapshot.ExecutionState is not (RecordState.Completed or RecordState.Failed or RecordState.Aborted or RecordState.Interrupted))
            throw new InvalidDataException($"不支持为执行状态 {snapshot.ExecutionState} 生成报表输入");
        if (snapshot.Purpose == ReportPurpose.ProductEvaluation
            && snapshot.ExecutionState != RecordState.Completed)
            throw new InvalidDataException("只有执行完成记录可以生成产品判定输入");
        if (snapshot.Metadata is null)
            throw new InvalidDataException("缺少报表元数据");
        if (!Enum.IsDefined(snapshot.Metadata.DeviceMode))
            throw new InvalidDataException("报表输入包含未知设备模式");
        if (snapshot.Metadata.Scope != snapshot.Scope || snapshot.Metadata.ExecutionState != snapshot.ExecutionState)
            throw new InvalidDataException("元数据范围/执行状态与报表输入顶层合同不一致");
        if (!Enum.IsDefined(snapshot.Metadata.Scope) || !Enum.IsDefined(snapshot.Metadata.ExecutionState))
            throw new InvalidDataException("元数据包含未知范围或执行状态");
        if (!string.Equals(snapshot.Metadata.RunId, snapshot.RunId, StringComparison.Ordinal))
            throw new InvalidDataException("元数据 RunId 与报表输入 RunId 不一致");
        if (snapshot.Metadata.StartedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("开始时间必须是 UTC");
        if (snapshot.Metadata.FinishedAtUtc is not { Kind: DateTimeKind.Utc })
            throw new InvalidDataException("终态报表输入必须包含 UTC 结束时间");
        if (string.IsNullOrWhiteSpace(snapshot.Metadata.RecordNumber)
            || string.IsNullOrWhiteSpace(snapshot.Metadata.ProductModelName)
            || string.IsNullOrWhiteSpace(snapshot.Metadata.OperatorName))
            throw new InvalidDataException("记录号、型号名称和操作员名称必须来自冻结的记录快照");
        if (string.IsNullOrWhiteSpace(snapshot.TargetTimeZoneId))
            throw new InvalidDataException("报表输入必须明确目标时区");

        var deviceModes = snapshot.DeviceModes ?? throw new InvalidDataException("缺少实际设备模式快照");
        if (deviceModes.Any(device => device is null))
            throw new InvalidDataException("设备模式列表不能包含空项");
        if (deviceModes.Any(device => string.IsNullOrWhiteSpace(device.DeviceId) || !Enum.IsDefined(device.Mode)
                                      || device.Mode == DeviceMode.Mixed))
            throw new InvalidDataException("实际设备模式快照包含无效设备或聚合模式");
        if (deviceModes.Select(device => device.DeviceId).Distinct(StringComparer.Ordinal).Count() != deviceModes.Count)
            throw new InvalidDataException("实际设备模式快照包含重复 DeviceId");

        var resources = snapshot.Resources ?? throw new InvalidDataException("缺少报表资源摘要");
        if (string.IsNullOrWhiteSpace(resources.TemplateKey) || string.IsNullOrWhiteSpace(resources.MappingKey))
            throw new InvalidDataException("模板和映射必须使用受控资源键");
        RequireHash(resources.TemplateHash, nameof(resources.TemplateHash));
        RequireHash(resources.MappingHash, nameof(resources.MappingHash));
        if ((resources.GuidanceKey is null) != (resources.GuidanceHash is null))
            throw new InvalidDataException("指导资源键和摘要必须同时为空或同时提供");
        if (resources.GuidanceHash is not null)
            RequireHash(resources.GuidanceHash, nameof(resources.GuidanceHash));

        var parameters = snapshot.Parameters ?? throw new InvalidDataException("缺少参数快照列表");
        if (parameters.Any(parameter => parameter is null))
            throw new InvalidDataException("参数列表不能包含空项");
        var duplicateParameter = parameters
            .GroupBy(parameter => parameter.Field, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateParameter is not null)
            throw new InvalidDataException($"固定参数字段重复：{duplicateParameter.Key}");
        foreach (var parameter in parameters)
            parameter.EnsureValid();

        var items = snapshot.Items ?? throw new InvalidDataException("缺少冻结项点列表");
        if (items.Any(item => item is null))
            throw new InvalidDataException("冻结项点列表不能包含空项");
        if (items.Count == 0)
            throw new InvalidDataException("报表输入不能缺少冻结项点");
        if (snapshot.Scope == RunScope.Single && items.Count != 1)
            throw new InvalidDataException("Single 范围必须恰好包含一个冻结项点");
        var duplicatePoint = items
            .GroupBy(item => item.PointId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePoint is not null)
            throw new InvalidDataException($"冻结项点重复：{duplicatePoint.Key}");

        foreach (var item in items)
        {
            if (item.PointId <= 0 || string.IsNullOrWhiteSpace(item.Name)
                || string.IsNullOrWhiteSpace(item.ExecutorCode) || item.OutputContractVersion <= 0)
                throw new InvalidDataException("冻结项点缺少有效身份或输出合同版本");
            if (!Enum.IsDefined(item.State))
                throw new InvalidDataException($"项点 {item.PointId} 包含未知执行状态");

            var measurements = item.Measurements ?? throw new InvalidDataException($"项点 {item.PointId} 缺少测量列表");
            if (measurements.Any(value => value is null))
                throw new InvalidDataException($"项点 {item.PointId} 的测量列表不能包含空项");
            var duplicateMeasurement = measurements
                .GroupBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateMeasurement is not null)
                throw new InvalidDataException($"项点 {item.PointId} 的测量键重复或仅大小写不同：{duplicateMeasurement.Key}");
            foreach (var measurement in measurements)
                measurement.EnsureValid();
        }

        if (snapshot.Purpose == ReportPurpose.ProductEvaluation
            && items.Any(item => item.State != ItemResultState.Succeeded))
            throw new InvalidDataException("产品判定输入要求冻结范围内所有项点均为 Succeeded");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new UtcRoundTripDateTimeConverter());
        return options;
    }

    private static void RequireGuid(string value, string field)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed)
            || !string.Equals(value, parsed.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException($"{field} 必须为小写 GUID D 格式");
    }

    private static void RequireHash(string value, string field)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))
            || value.Any(char.IsUpper))
            throw new InvalidDataException($"{field} 必须为 64 位小写 SHA-256");
    }

    private sealed class UtcRoundTripDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (!DateTime.TryParseExact(text, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var value)
                || value.Kind != DateTimeKind.Utc)
                throw new JsonException("时间必须是 UTC O 格式");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            if (value.Kind != DateTimeKind.Utc)
                throw new JsonException("时间必须是 UTC");
            writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
        }
    }
}
