namespace XXX.TestBench.Core.Domain.TestDefinitions;

/// <summary>可复用试验项定义。项点代码唯一；执行器代码标识具体试验算法扩展点。</summary>
public sealed class TestItemDefinition
{
    public int Id { get; set; }
    public required string Code { get; init; }
    public required string Name { get; set; }
    public required string ExecutorCode { get; set; }
    public required string ResultKind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}
