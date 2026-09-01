namespace XXX.TestBench.Core.Domain.Products;

/// <summary>产品型号，必须属于一个产品类型；型号代码在产品类型内唯一。</summary>
public sealed class ProductModel
{
    public int Id { get; set; }
    public int ProductTypeId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; init; }
}
