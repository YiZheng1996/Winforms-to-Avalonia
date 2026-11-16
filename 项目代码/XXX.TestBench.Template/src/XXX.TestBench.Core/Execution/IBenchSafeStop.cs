namespace XXX.TestBench.Core.Execution;

public enum StopReason
{
    UserRequested = 0,
    ExecutionFailure = 1,
    CommunicationFailure = 2,
    ApplicationExit = 3,
    SelfCheckCancelled = 4,
    Completed = 5
}

public enum SafeStopDisposition
{
    Confirmed = 0,
    Uncertain = 1,
    Failed = 2
}

public sealed record SafeStopActionResult(
    string ActionCode,
    SafeStopDisposition Disposition,
    string Message,
    string? EvidenceKey);

public sealed record SafeStopResult(
    SafeStopDisposition Disposition,
    IReadOnlyList<SafeStopActionResult> Actions,
    string Message,
    string? OperatorAdvice);

/// <summary>
/// 台架项目专属的受控收尾实现。具体动作和顺序必须由项目资料确定。
/// </summary>
public interface IBenchSafeStop
{
    Task<SafeStopResult> StopAsync(
        BenchOperationContext context,
        StopReason reason,
        CancellationToken cleanupToken);
}
