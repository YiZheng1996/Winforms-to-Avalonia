using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 产线协同扩展合同。当前模板不注册实现，也不在试验流程中调用。
/// </summary>
public interface IProductionLineGateway
{
    Task<LineReadiness> CheckReadinessAsync(
        LineTestIdentity identity,
        CancellationToken cancellationToken = default);

    Task PublishCompletedAsync(
        CompletedTestEnvelope result,
        CancellationToken cancellationToken = default);
}

public enum LineReadinessState
{
    Unavailable = 0,
    NotIntegrated = 1,
    Allowed = 2,
    Rejected = 3
}

public sealed record LineReadiness(
    LineReadinessState State,
    string Message,
    string? EvidenceKey);

public sealed record LineTestIdentity(
    string BenchId,
    string RunId,
    string? ProductNumber,
    string ProductModelName,
    int ProtocolVersion);

public sealed record CompletedTestEnvelope(
    string BenchId,
    string RunId,
    RecordState ExecutionState,
    EvaluationState? EvaluationState,
    string? EvaluationText,
    DateTime FinishedAtUtc,
    string? ReportStorageKey,
    string? ReportSha256,
    int ProtocolVersion);
