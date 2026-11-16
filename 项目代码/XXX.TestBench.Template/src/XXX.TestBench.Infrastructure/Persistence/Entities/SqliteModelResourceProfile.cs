using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>model_resource_profiles 表映射。</summary>
[Table(Name = "model_resource_profiles")]
internal sealed class SqliteModelResourceProfile
{
    [Column(Name = "product_model_id", IsPrimary = true)] public int ProductModelId { get; set; }
    [Column(Name = "template_key", IsNullable = false)] public string TemplateKey { get; set; } = string.Empty;
    [Column(Name = "mapping_key", IsNullable = false)] public string MappingKey { get; set; } = string.Empty;
    [Column(Name = "process_template_key")] public string? ProcessTemplateKey { get; set; }
    [Column(Name = "process_mapping_key")] public string? ProcessMappingKey { get; set; }
    [Column(Name = "guidance_key")] public string? GuidanceKey { get; set; }
    [Column(Name = "revision", IsNullable = false)] public long Revision { get; set; }
    [Column(Name = "updated_by", IsNullable = false)] public string UpdatedBy { get; set; } = string.Empty;
    [Column(Name = "updated_at_utc", IsNullable = false)] public string UpdatedAtUtc { get; set; } = string.Empty;
    [Column(Name = "row_version", IsNullable = false)] public long RowVersion { get; set; }
}
