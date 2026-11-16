using XXX.TestBench.Core.Domain.Execution;
using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// UI 可读取的不可变运行状态，不包含控件、锁、取消源或设备运行时。
/// </summary>
public sealed record RunSnapshot(
    Guid? OperationId,
    Guid? RunId,
    Guid? PreparationSessionId,
    string BenchId,
    BenchPhase RunState,
    IReadOnlyList<string> RecoveryReasons,
    RunScope? Scope,
    int? CurrentPointId,
    int CompletedPointCount,
    int TotalPointCount,
    string? CurrentStep,
    DateTime? StartedAtUtc,
    TimeSpan Elapsed,
    string StatusMessage,
    bool CanStart,
    bool CanStop);

/// <summary>
/// 单项进度，不以估算百分比代替执行器实际进度。
/// </summary>
public sealed record RunProgress(
    Guid OperationId,
    int? PointId,
    string? Step,
    int CompletedPointCount,
    int TotalPointCount,
    string Message,
    DateTime ReportedAtUtc);
