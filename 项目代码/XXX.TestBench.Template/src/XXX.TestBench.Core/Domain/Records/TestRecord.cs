using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 一次实际执行记录：直接关联产品型号，并快照产品标识、设备模式、操作员、时间、
/// 直编参数 JSON 与本次执行的试验项点序列。执行过程中只读快照，不读取界面控件。
/// </summary>
public sealed class TestRecord
{
    /// <summary>
    /// 记录编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 记录流水号。
    /// </summary>
    public required string RecordNumber { get; init; }
    /// <summary>
    /// 试验产品型号编号。
    /// </summary>
    public required int ProductModelId { get; init; }
    /// <summary>
    /// 产品标识快照。
    /// </summary>
    public required ProductIdentity ProductIdentity { get; init; }
    /// <summary>
    /// 试验参数快照。
    /// </summary>
    public string? ParameterSnapshot { get; init; }
    /// <summary>
    /// 试验项点序列快照。
    /// </summary>
    public string? SequenceSnapshot { get; init; }
    /// <summary>
    /// 试验时的设备模式。
    /// </summary>
    public DeviceMode DeviceMode { get; init; }
    /// <summary>
    /// 操作员用户编号。
    /// </summary>
    public int OperatorUserId { get; init; }
    /// <summary>
    /// 记录整体状态。
    /// </summary>
    public RecordState State { get; internal set; } = RecordState.Running;
    /// <summary>
    /// 最终结论或失败原因。
    /// </summary>
    public string? Conclusion { get; set; }
    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime StartedAtUtc { get; init; }
    /// <summary>
    /// 结束时间。
    /// </summary>
    public DateTime? FinishedAtUtc { get; internal set; }

    /// <summary>
    /// 标记记录为已完成。
    /// </summary>
    public void Complete(DateTime utcNow, string conclusion)
    {
        if (State != RecordState.Running) throw new DomainException($"记录当前状态 {State}，不能完成");
        State = RecordState.Completed;
        Conclusion = conclusion;
        FinishedAtUtc = utcNow;
    }

    /// <summary>
    /// 标记记录为失败。
    /// </summary>
    public void Fail(DateTime utcNow, string reason)
    {
        if (State != RecordState.Running) throw new DomainException($"记录当前状态 {State}，不能标记失败");
        State = RecordState.Failed;
        Conclusion = reason;
        FinishedAtUtc = utcNow;
    }

    /// <summary>
    /// 标记记录为已中止。
    /// </summary>
    public void Abort(DateTime utcNow, string reason)
    {
        if (State != RecordState.Running) throw new DomainException($"记录当前状态 {State}，不能中止");
        State = RecordState.Aborted;
        Conclusion = reason;
        FinishedAtUtc = utcNow;
    }
}
