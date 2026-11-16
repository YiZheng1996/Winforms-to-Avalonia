namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 产品结论状态。执行状态与产品判定分开保存。
/// 数值是持久化合同，已经发布后不得重排。
/// </summary>
public enum EvaluationState
{
    Pending = 0,
    Qualified = 1,
    Unqualified = 2,
    Error = 3,
    NotEvaluated = 4,
    Legacy = 5
}
