using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 单个试验项点的执行结果，引用记录内固化序列中的试验项点。
/// </summary>
public sealed class TestItemResult
{
    /// <summary>
    /// 结果记录编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 所属试验记录编号。
    /// </summary>
    public int RecordId { get; init; }
    /// <summary>
    /// 对应的试验项点编号。
    /// </summary>
    public int TestItemPointId { get; init; }
    /// <summary>
    /// 当前执行状态。
    /// </summary>
    public ItemResultState State { get; internal set; } = ItemResultState.Pending;
    /// <summary>
    /// 结果汇总值，用于报表展示。
    /// </summary>
    public string? SummaryValue { get; set; }
    /// <summary>
    /// 结果文字说明。
    /// </summary>
    public string? ResultText { get; set; }
    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime? StartedAtUtc { get; set; }
    /// <summary>
    /// 结束时间。
    /// </summary>
    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>
    /// 标记开始执行；已结束的项点不允许重新开始。
    /// </summary>
    public void Start(DateTime utcNow)
    {
        if (State is ItemResultState.Passed or ItemResultState.Failed or ItemResultState.Aborted)
            throw new DomainException($"项点结果当前状态 {State}，不能重新开始");
        State = ItemResultState.Running;
        StartedAtUtc ??= utcNow;
    }

    /// <summary>
    /// 写入最终结果与时间。
    /// </summary>
    public void SetResult(ItemResultState final, string? summaryValue, string? resultText, DateTime utcNow)
    {
        if (final is not (ItemResultState.Passed or ItemResultState.Failed or ItemResultState.Skipped or ItemResultState.Aborted))
            throw new DomainException($"不支持的终态 {final}");
        State = final;
        SummaryValue = summaryValue;
        ResultText = resultText;
        FinishedAtUtc = utcNow;
    }
}
