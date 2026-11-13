namespace XXX.TestBench.Core.Domain.Products;

/// <summary>
/// 产品类型信息。
/// </summary>
public sealed class ProductType
{
    /// <summary>
    /// 产品类型编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 产品类型名称。
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
