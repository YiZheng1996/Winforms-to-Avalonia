namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 试验记录整体状态。
/// </summary>
public enum RecordState
{
    /// <summary>
    /// 正在执行。
    /// </summary>
    Running = 0,
    /// <summary>
    /// 已完成。
    /// </summary>
    Completed = 1,
    /// <summary>
    /// 失败。
    /// </summary>
    Failed = 2,
    /// <summary>
    /// 人为中止。
    /// </summary>
    Aborted = 3,
    /// <summary>
    /// 进程或主机异常中断；不能按完成或产品不合格处理。
    /// </summary>
    Interrupted = 4
}
