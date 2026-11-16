using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>
/// JSON 映射资源的可持久化合同。模板和映射摘要由资源仓储绑定，避免映射文件自引用自身摘要。
/// </summary>
public sealed record ReportTemplateMappingV1
{
    public int SchemaVersion { get; init; } = 1;
    public required string TemplateId { get; init; }
    public required string Version { get; init; }
    public ReportPurpose Purpose { get; init; }
    public required IReadOnlyList<RunScope> SupportedRunScopes { get; init; }
    public required IReadOnlyList<ReportInputDefinition> Inputs { get; init; }
    public ReportConclusionDefinition? ProductConclusion { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    /// <summary>
    /// 从冻结映射字节解析合同，并绑定已核对的模板/映射摘要。
    /// </summary>
    public static ReportTemplateDefinition Deserialize(
        ReadOnlySpan<byte> utf8Json,
        string templateHash,
        string mappingHash)
    {
        if (utf8Json.IsEmpty)
            throw new InvalidDataException("报表映射文件为空。");
        if (utf8Json.StartsWith(Encoding.UTF8.Preamble))
            throw new InvalidDataException("报表映射必须是无 BOM 的 UTF-8 JSON。");

        var mapping = JsonSerializer.Deserialize<ReportTemplateMappingV1>(utf8Json, SerializerOptions)
            ?? throw new InvalidDataException("报表映射内容为空。");
        if (mapping.SchemaVersion != 1)
            throw new InvalidDataException($"不支持的报表映射版本：{mapping.SchemaVersion}。");

        var definition = new ReportTemplateDefinition
        {
            TemplateId = mapping.TemplateId,
            Version = mapping.Version,
            TemplateHash = templateHash,
            MappingHash = mappingHash,
            Purpose = mapping.Purpose,
            SupportedRunScopes = mapping.SupportedRunScopes,
            Inputs = mapping.Inputs,
            ProductConclusion = mapping.ProductConclusion
        };
        var errors = definition.ValidateContract();
        if (errors.Count > 0)
            throw new InvalidDataException("报表映射合同无效：" + string.Join("；", errors));
        return definition;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
