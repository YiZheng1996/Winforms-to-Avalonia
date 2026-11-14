using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 报表计算适配器边界。实现负责解析冻结资源并调用经过验证的计算引擎；本接口不提供默认/仿真成功实现。
/// </summary>
public interface IReportArtifactProducer
{
    Task<ReportArtifactProductionResult> ProduceAsync(
        ReportArtifactProductionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 只传稳定标识与冻结 JSON 原文，不把可变的数据库实体交给计算适配器。
/// </summary>
public sealed record ReportArtifactProductionRequest(
    Guid RevisionId,
    int TestRecordId,
    string InputHash,
    string FrozenInputJson,
    string AttemptToken);

/// <summary>
/// 计算适配器返回的未归档产物。Workbook.Content 的所有权交给调用方，由归档 worker 在成功或失败后释放。
/// </summary>
public sealed record ReportArtifactProductionResult(
    bool Succeeded,
    Guid RevisionId,
    string InputHash,
    string? EngineName,
    string? EngineVersion,
    ReportTemplateDefinition? TemplateDefinition,
    string? RawConclusion,
    string? CalculationResultJson,
    ReportArtifactContent? Workbook,
    string? ErrorStage,
    string? Error);
