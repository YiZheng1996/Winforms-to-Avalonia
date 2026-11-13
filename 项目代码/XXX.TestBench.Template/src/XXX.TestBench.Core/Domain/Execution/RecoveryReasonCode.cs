namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>
/// 可同时存在的恢复原因代码。代码保存为字符串，新增原因不会重排其他代码。
/// </summary>
public static class RecoveryReasonCode
{
    public const string SafetyUnconfirmed = "SafetyUnconfirmed";
    public const string InterruptedOperation = "InterruptedOperation";
    public const string PersistencePending = "PersistencePending";
}
