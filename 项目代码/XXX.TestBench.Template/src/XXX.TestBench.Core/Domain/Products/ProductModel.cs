namespace XXX.TestBench.Core.Domain.Products;

/// <summary>
/// 产品型号，必须属于一个产品类型；通过 ProductTypeId 关联所属产品类型。
/// </summary>
public sealed class ProductModel
{
    /// <summary>
    /// 型号编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 所属产品类型编号。
    /// </summary>
    public int ProductTypeId { get; init; }
    /// <summary>
    /// 型号名称。
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// 是否启用。
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }
}
