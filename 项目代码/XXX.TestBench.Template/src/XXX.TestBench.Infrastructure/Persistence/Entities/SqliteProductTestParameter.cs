using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 产品级试验参数表的 SQLite 映射实体。
/// </summary>
[Table(Name = "product_test_parameters")]
internal sealed class SqliteProductTestParameter
{
    /// <summary>
    /// 所属产品型号编号，同时也是参数记录主键。
    /// </summary>
    [Column(Name = "product_model_id", IsPrimary = true)]
    public int ProductModelId { get; set; }

    /// <summary>
    /// 所属产品类型编号，用于明确参数的产品组合范围。
    /// </summary>
    [Column(Name = "product_type_id")]
    public int ProductTypeId { get; set; }

    /// <summary>
    /// 试验电压，单位为伏特。
    /// </summary>
    [Column(Name = "test_voltage_v")]
    public double TestVoltageV { get; set; }

    /// <summary>
    /// 保护电流，单位为毫安。
    /// </summary>
    [Column(Name = "protect_current_ma")]
    public double ProtectCurrentMa { get; set; }

    /// <summary>
    /// 最后修改参数的用户登录名。
    /// </summary>
    [Column(Name = "updated_by")]
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 最后更新时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = string.Empty;
}
