using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Domain.Tasks;

/// <summary>
/// 一次实际执行记录，关联任务并快照设备模式、操作员、时间与直编参数 JSON。
/// 执行过程中只读该快照，不读取界面控件。
/// </summary>
public sealed class TestRecord
{
    public int Id { get; set; }
    public int TaskId { get; init; }
    public string? ParameterSnapshot { get; init; }
    public DeviceMode DeviceMode { get; init; }
    public int OperatorUserId { get; init; }
    public RecordState State { get; internal set; } = RecordState.Running;
    public string? Conclusion { get; set; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? FinishedAtUtc { get; internal set; }

    public void Complete(DateTime utcNow, string conclusion)
    {
        if (State != RecordState.Running) throw new DomainException($"记录当前状态 {State}，不能完成");
        State = RecordState.Completed;
        Conclusion = conclusion;
        FinishedAtUtc = utcNow;
    }

    public void Fail(DateTime utcNow, string reason)
    {
        if (State != RecordState.Running) throw new DomainException($"记录当前状态 {State}，不能标记失败");
        State = RecordState.Failed;
        Conclusion = reason;
        FinishedAtUtc = utcNow;
    }

    public void Abort(DateTime utcNow, string reason)
    {
        if (State != RecordState.Running) throw new DomainException($"记录当前状态 {State}，不能中止");
        State = RecordState.Aborted;
        Conclusion = reason;
        FinishedAtUtc = utcNow;
    }
}
