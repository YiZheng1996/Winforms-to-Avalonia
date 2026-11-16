namespace XXX.TestBench.Core.Execution;

public enum CheckStatus
{
    Passed = 0,
    Failed = 1,
    Unknown = 2,
    Cancelled = 3
}

public sealed record SelfCheckResult(
    string CheckId,
    string Name,
    bool Required,
    CheckStatus Status,
    string Message,
    string? Advice,
    DateTime CompletedAtUtc);

/// <summary>
/// 项目负责人实现的自检合同；检查项清单及 Required 属性由协调器冻结，不能由结果降级。
/// </summary>
public interface IBenchSelfCheck
{
    string CheckId { get; }
    string Name { get; }
    bool Required { get; }

    Task<SelfCheckResult> ExecuteAsync(
        BenchOperationContext context,
        CancellationToken cancellationToken);
}
