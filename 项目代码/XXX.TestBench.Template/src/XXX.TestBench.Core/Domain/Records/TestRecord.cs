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
    /// 稳定运行标识；当前版本新建记录必须提供，历史导入也由离线工具分配稳定标识。
    /// </summary>
    public string? RunId { get; init; }
    /// <summary>
    /// 开始请求标识，用于查证提交结果未知的重试。
    /// </summary>
    public string? StartRequestId { get; init; }
    /// <summary>
    /// 本次准备/执行操作标识。
    /// </summary>
    public string? OperationId { get; init; }
    /// <summary>
    /// 试验台和控制节点身份；旧版记录为空。
    /// </summary>
    public string? BenchId { get; init; }
    public string? NodeId { get; init; }
    /// <summary>
    /// 试验产品型号编号。
    /// </summary>
    public required int ProductModelId { get; init; }
    /// <summary>
    /// 产品标识快照。
    /// </summary>
    public required ProductIdentity ProductIdentity { get; init; }
    /// <summary>
    /// 记录创建时的型号名称，避免主数据变更破坏历史展示。
    /// </summary>
    public string? ModelNameSnapshot { get; init; }
    /// <summary>
    /// 记录创建时的操作员名称。
    /// </summary>
    public string? OperatorNameSnapshot { get; init; }
    /// <summary>
    /// 结果合同版本；旧数据按版本 1 只读。
    /// </summary>
    public int ResultContractVersion { get; init; } = 2;
    /// <summary>
    /// 本次冻结的执行范围；旧版记录为空。
    /// </summary>
    public RunScope? Scope { get; init; }
    /// <summary>
    /// 可选的复测来源 RunId。
    /// </summary>
    public string? PreviousRunId { get; init; }
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
    /// 创建记录时使用的完整设备配置版本。
    /// </summary>
    public string? DeviceConfigurationRevision { get; init; }
    /// <summary>
    /// 创建记录时解析出的 SignalKey → PointId 映射 JSON 快照。
    /// </summary>
    public string? SignalBindingsSnapshot { get; init; }
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
    /// Excel 判定原文；旧版本按迁移记录保留，不在此字段重算。
    /// </summary>
    public string? EvaluationText { get; set; }
    /// <summary>
    /// 产品判定状态；空值用于无法证明判定来源的旧记录。
    /// </summary>
    public EvaluationState? EvaluationState { get; set; }
    /// <summary>
    /// 结论来源的稳定代码，由对应合同定义，不使用自由文本推断。
    /// </summary>
    public string? ConclusionSource { get; set; }
    /// <summary>
    /// 执行故障/中止/中断原因，与产品结论分开保存。
    /// </summary>
    public string? ExecutionError { get; set; }
    /// <summary>
    /// 型号资源及其摘要快照。
    /// </summary>
    public string? ResourceSnapshotJson { get; init; }
    /// <summary>
    /// 准备、自检及设备前置状态快照。
    /// </summary>
    public string? PrecheckSnapshotJson { get; init; }
    /// <summary>
    /// 完成时冻结的报表输入原文及其摘要。
    /// </summary>
    public string? ReportInputJson { get; set; }
    public string? InputHash { get; set; }
    public string? ExecutionEvidenceKey { get; set; }
    public string? CurrentReportRevisionId { get; set; }
    public long RowVersion { get; set; }
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
