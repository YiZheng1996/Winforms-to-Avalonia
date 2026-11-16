namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>
/// 绑定到单个产品型号的不可变模板、映射和准备指导资源键。
/// 每次导入都使用新资源键，避免旧试验被覆盖到新版本。
/// </summary>
public sealed record ModelResourceProfile
{
    public required int ProductModelId { get; init; }
    public required string TemplateKey { get; init; }
    public required string MappingKey { get; init; }
    public string? ProcessTemplateKey { get; init; }
    public string? ProcessMappingKey { get; init; }
    public string? GuidanceKey { get; init; }
    public required long Revision { get; init; }
    public required string UpdatedBy { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
    public long RowVersion { get; set; }

    /// <summary>校验数据库保存前的型号、版本和资源键合同。</summary>
    public void EnsureValid()
    {
        if (ProductModelId <= 0) throw new InvalidDataException("资源配置的产品型号编号无效。");
        if (string.IsNullOrWhiteSpace(TemplateKey) || string.IsNullOrWhiteSpace(MappingKey))
            throw new InvalidDataException("产品判定模板和映射必须同时配置。");
        if (string.IsNullOrWhiteSpace(ProcessTemplateKey) != string.IsNullOrWhiteSpace(ProcessMappingKey))
            throw new InvalidDataException("过程报告模板和映射必须同时配置。");
        if (Revision <= 0 || RowVersion < 0) throw new InvalidDataException("资源配置版本无效。");
        if (string.IsNullOrWhiteSpace(UpdatedBy)) throw new InvalidDataException("资源配置操作人不能为空。");
        if (UpdatedAtUtc.Kind != DateTimeKind.Utc) throw new InvalidDataException("资源配置时间必须使用 UTC。");
        foreach (var key in new[] { TemplateKey, MappingKey, ProcessTemplateKey, ProcessMappingKey, GuidanceKey })
            if (key is { Length: > 512 } || key?.Any(char.IsControl) == true)
                throw new InvalidDataException("资源键过长或包含控制字符。");
    }
}
