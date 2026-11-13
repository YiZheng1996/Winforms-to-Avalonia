namespace XXX.TestBench.Core.Domain.TestPoints;

/// <summary>
/// 产品类型下的试验项点。ProductTypeId 关联所属产品类型；ExecutorCode 指向编译期注册的
/// ITestItemExecutor（关联逻辑类），运行时只能从已注册执行器中选择，不能生成任意逻辑。
/// </summary>
public sealed class TestItemPoint
{
    /// <summary>
    /// 项点编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 所属产品类型编号。
    /// </summary>
    public required int ProductTypeId { get; init; }
    /// <summary>
    /// 项点名称。
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// 执行器代码，决定该项点的执行逻辑。
    /// </summary>
    public required string ExecutorCode { get; set; }
    /// <summary>
    /// 结果类型。
    /// </summary>
    public string ResultKind { get; set; } = "PassFail";
    /// <summary>
    /// 是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>
    /// 排序序号。
    /// </summary>
    public int SortOrder { get; set; }
    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }
    /// <summary>
    /// 更新时间。
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}
