using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 试验项点表的 SQLite 映射实体。
/// </summary>
[Table(Name = "test_item_points")]
internal sealed class SqliteTestItemPoint
{
    /// <summary>
    /// 试验项点主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 所属产品类型编号。
    /// </summary>
    [Column(Name = "product_type_id")]
    public int ProductTypeId { get; set; }

    /// <summary>
    /// 试验项点业务编码。
    /// </summary>
    [Column(Name = "code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 试验项点名称。
    /// </summary>
    [Column(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 执行器编码，用于定位实际执行设备。
    /// </summary>
    [Column(Name = "executor_code")]
    public string ExecutorCode { get; set; } = string.Empty;

    /// <summary>
    /// 结果类型编码。
    /// </summary>
    [Column(Name = "result_kind")]
    public string ResultKind { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用，1 表示启用。
    /// </summary>
    [Column(Name = "is_enabled")]
    public int IsEnabled { get; set; }

    /// <summary>
    /// 在产品类型中的显示顺序。
    /// </summary>
    [Column(Name = "sort_order")]
    public int SortOrder { get; set; }

    /// <summary>
    /// 创建时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "created_at_utc")]
    public string CreatedAtUtc { get; set; } = string.Empty;

    /// <summary>
    /// 最后更新时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = string.Empty;
}
