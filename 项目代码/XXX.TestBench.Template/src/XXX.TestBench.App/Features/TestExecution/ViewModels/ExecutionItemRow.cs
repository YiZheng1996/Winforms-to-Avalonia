namespace XXX.TestBench.App.ViewModels;

/// <summary>执行页的只读项点展示；运行协调器接入后从运行快照填充。</summary>
public sealed record ExecutionItemRow
{
    public required int PointId { get; init; }
    public required string Name { get; init; }
    public required string ExecutorCode { get; init; }
    public required int SortOrder { get; init; }
    public string StateText { get; init; } = "未执行";
    public string? Summary { get; init; }
    public string? ResultText { get; init; }
}
