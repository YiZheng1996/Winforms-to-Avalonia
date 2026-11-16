namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 本次执行冻结的范围。数值是持久化合同，已经发布后不得重排。
/// </summary>
public enum RunScope
{
    Auto = 0,
    Single = 1
}
