using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Domain.Tasks;

/// <summary>
/// 本地手工创建的待执行任务。TestTask 是待执行工作，TestRecord 是一次实际执行。
/// 任务只关联产品型号；试验项顺序由代码固定，参数在启动时固化为快照。
/// </summary>
public sealed class TestTask
{
    public int Id { get; set; }
    public required string TaskNumber { get; init; }
    public int ProductModelId { get; init; }
    public ProductIdentity ProductIdentity { get; set; } = new ProductIdentity(null, null, null, null);
    public TaskState State { get; internal set; } = TaskState.Draft;
    public int CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; internal set; }
    public DateTime? FinishedAtUtc { get; internal set; }

    public void ToReady()
    {
        if (State != TaskState.Draft) throw new DomainException($"任务当前状态 {State}，不能进入就绪");
        State = TaskState.Ready;
    }

    public void Start(DateTime utcNow)
    {
        if (State != TaskState.Ready) throw new DomainException($"任务当前状态 {State}，不能启动");
        State = TaskState.Running;
        StartedAtUtc = utcNow;
    }

    public void Complete(DateTime utcNow)
    {
        if (State != TaskState.Running) throw new DomainException($"任务当前状态 {State}，不能完成");
        State = TaskState.Completed;
        FinishedAtUtc = utcNow;
    }

    public void Fail(DateTime utcNow)
    {
        if (State != TaskState.Running && State != TaskState.Ready) throw new DomainException($"任务当前状态 {State}，不能标记失败");
        State = TaskState.Failed;
        FinishedAtUtc = utcNow;
    }

    public void Cancel(DateTime utcNow)
    {
        if (State is TaskState.Completed or TaskState.Failed or TaskState.Cancelled)
            throw new DomainException($"任务当前状态 {State}，不能取消");
        State = TaskState.Cancelled;
        FinishedAtUtc = utcNow;
    }
}
