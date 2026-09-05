using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 产品型号与试验项点配置表的 SQLite 映射实体。
/// </summary>
[Table(Name = "model_point_configs")]
internal sealed class SqliteModelPointConfig
{
    /// <summary>
    /// 配置记录主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 产品型号编号。
    /// </summary>
    [Column(Name = "product_model_id")]
    public int ProductModelId { get; set; }

    /// <summary>
    /// 试验项点编号。
    /// </summary>
    [Column(Name = "test_item_point_id")]
    public int TestItemPointId { get; set; }

    /// <summary>
    /// 试验项点在产品型号中的执行顺序。
    /// </summary>
    [Column(Name = "sort_order")]
    public int SortOrder { get; set; }

    /// <summary>
    /// 配置时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "configured_at_utc")]
    public string ConfiguredAtUtc { get; set; } = string.Empty;
}
