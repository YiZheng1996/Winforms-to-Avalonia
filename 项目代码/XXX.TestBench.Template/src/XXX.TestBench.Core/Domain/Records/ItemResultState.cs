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
    /// 结果合同版本 1 的历史项点完成状态；不作为新版产品判定。
    /// </summary>
    Passed = 2,
    /// <summary>
    /// 历史/新版项点执行失败状态；新版产品结论由报表评估单独保存。
    /// </summary>
    Failed = 3,
    /// <summary>
    /// 已跳过。
    /// </summary>
    Skipped = 4,
    /// <summary>
    /// 已中止。
    /// </summary>
    Aborted = 5,
    /// <summary>
    /// 执行器已成功完成该项；不表示产品合格。
    /// </summary>
    Succeeded = 6
}
