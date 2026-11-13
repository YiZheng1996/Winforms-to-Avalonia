using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 试验项点结果表的 SQLite 映射实体。
/// </summary>
[Table(Name = "test_item_results")]
internal sealed class SqliteTestItemResult
{
    /// <summary>
    /// 结果记录主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 所属试验记录编号。
    /// </summary>
    [Column(Name = "record_id")]
    public int RecordId { get; set; }

    /// <summary>
    /// 试验项点编号。
    /// </summary>
    [Column(Name = "test_item_point_id")]
    public int TestItemPointId { get; set; }

    /// <summary>
    /// 项点执行状态枚举值。
    /// </summary>
    [Column(Name = "state")]
    public int State { get; set; }

    /// <summary>
    /// 结果汇总文本，可为空。
    /// </summary>
    [Column(Name = "summary_value", IsNullable = true)]
    public string? SummaryValue { get; set; }

    /// <summary>
    /// 项点结果文本，可为空。
    /// </summary>
    [Column(Name = "result_text", IsNullable = true)]
    public string? ResultText { get; set; }

    /// <summary>
    /// 项点开始时间，使用 UTC 文本保存；未开始时为空。
    /// </summary>
    [Column(Name = "started_at_utc", IsNullable = true)]
    public string? StartedAtUtc { get; set; }

    /// <summary>
    /// 项点完成时间，使用 UTC 文本保存；未完成时为空。
    /// </summary>
    [Column(Name = "finished_at_utc", IsNullable = true)]
    public string? FinishedAtUtc { get; set; }
}
