namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 单个试验项的结果状态。
/// </summary>
public enum ItemResultState
{
    /// <summary>
    /// 等待执行。
    /// </summary>
    Pending = 0,
    /// <summary>
    /// 正在执行。
    /// </summary>
    Running = 1,
    /// <summary>
    /// 合格。
    /// </summary>
    Passed = 2,
    /// <summary>
    /// 不合格。
    /// </summary>
    Failed = 3,
    /// <summary>
    /// 已跳过。
    /// </summary>
    Skipped = 4,
    /// <summary>
    /// 已中止。
    /// </summary>
    Aborted = 5
}
