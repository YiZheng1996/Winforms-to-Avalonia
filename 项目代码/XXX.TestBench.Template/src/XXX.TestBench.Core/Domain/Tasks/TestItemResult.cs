using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Domain.Tasks;

/// <summary>
/// 单个项点执行结果；试验项来自代码固定序列，故只保存试验项定义 ID，不再关联配方项。
/// </summary>
public sealed class TestItemResult
{
    public int Id { get; set; }
    public int RecordId { get; init; }
    public int? RecipeItemId { get; init; }
    public int TestItemDefinitionId { get; init; }
    public ItemResultState State { get; internal set; } = ItemResultState.Pending;
    public string? SummaryValue { get; set; }
    public string? ResultText { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }

    public void Start(DateTime utcNow)
    {
        if (State is ItemResultState.Passed or ItemResultState.Failed or ItemResultState.Aborted)
            throw new DomainException($"项点结果当前状态 {State}，不能重新开始");
        State = ItemResultState.Running;
        StartedAtUtc ??= utcNow;
    }

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
