using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 独立报表引擎。结果必须携带输入摘要、引擎版本、原始结论和输出摘要，不能只返回成功布尔值。
/// </summary>
public interface IReportCalculationEngine
{
    Task<ReportCalculationResult> CalculateAsync(
        ReportCalculationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ReportCalculationRequest(
    Guid RevisionId,
    string InputHash,
    string InputJsonPath,
    string InputWorkbookPath,
    string TemplatePath,
    string MappingPath,
    ReportTemplateDefinition TemplateDefinition,
    bool GeneratePdf,
    TimeSpan Timeout);

public sealed record ReportCalculationResult(
    bool CalculationSucceeded,
    Guid RevisionId,
    string InputHash,
    string EngineName,
    string EngineVersion,
    string? CalculatedWorkbookPath,
    string? CalculatedWorkbookHash,
    string? PdfPath,
    string? PdfHash,
    string? RawConclusion,
    string? ErrorStage,
    string? Error);
