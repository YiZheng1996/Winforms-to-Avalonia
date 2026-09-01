using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Domain.Products;

public sealed class ProductType
{
    public int Id { get; set; }
    public required string Code { get; init; }
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; init; }
}
