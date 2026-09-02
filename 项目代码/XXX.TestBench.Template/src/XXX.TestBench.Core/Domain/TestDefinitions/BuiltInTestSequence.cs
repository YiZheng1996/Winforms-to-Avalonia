namespace XXX.TestBench.Core.Domain.TestDefinitions;

/// <summary>
/// 代码固定的试验项序列项。模板执行流程由代码固定，不允许运行时编排。
/// </summary>
public sealed record BuiltInTestItem(string Code, string Name, string ExecutorCode, string ResultKind, int SortOrder);

/// <summary>
/// 模板内建的固定试验项序列。数据库只保存由本序列种子化后的定义副本，供结果与报表引用 ID。
/// 具体项目可通过扩展本列表或实现 ITestItemExecutor 注册新执行算法，但不能在运行时生成任意 C#。
/// </summary>
public static class BuiltInTestSequence
{
    /// <summary>固定执行顺序的试验项列表。</summary>
    public static IReadOnlyList<BuiltInTestItem> Items { get; } = new List<BuiltInTestItem>
    {
        new("PRESSURE", "耐压试验", "PressureExecutor", "PassFail", 1)
    };
}
