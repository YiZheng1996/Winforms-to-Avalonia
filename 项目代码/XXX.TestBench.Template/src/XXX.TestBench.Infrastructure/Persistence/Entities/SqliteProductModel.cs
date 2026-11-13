using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 产品型号表的 SQLite 映射实体。
/// </summary>
[Table(Name = "product_models")]
internal sealed class SqliteProductModel
{
    /// <summary>
    /// 产品型号主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 所属产品类型编号。
    /// </summary>
    [Column(Name = "product_type_id")]
    public int ProductTypeId { get; set; }

    /// <summary>
    /// 产品型号业务编码。
    /// </summary>
    [Column(Name = "code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 产品型号名称。
    /// </summary>
    [Column(Name = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用，1 表示启用。
    /// </summary>
    [Column(Name = "is_enabled")]
    public int IsEnabled { get; set; }

    /// <summary>
    /// 创建时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "created_at_utc")]
    public string CreatedAtUtc { get; set; } = string.Empty;
}
