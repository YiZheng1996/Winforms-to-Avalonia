using XXX.TestBench.Core.Domain.Products;

namespace XXX.TestBench.Core.Ports;

public interface IProductRepository
{
    Task<ProductType?> GetTypeByCodeAsync(string code, CancellationToken ct = default);
    Task<ProductType?> GetTypeAsync(int id, CancellationToken ct = default);
    Task<ProductModel?> GetModelAsync(int id, CancellationToken ct = default);
    Task<ProductModel?> GetModelByCodeAsync(int productTypeId, string code, CancellationToken ct = default);
    Task<IReadOnlyList<ProductType>> ListTypesAsync(bool includeDisabled, CancellationToken ct = default);
    Task<IReadOnlyList<ProductModel>> ListModelsAsync(int? productTypeId, bool includeDisabled, CancellationToken ct = default);
    Task AddTypeAsync(ProductType type, CancellationToken ct = default);
    Task AddModelAsync(ProductModel model, CancellationToken ct = default);
    Task UpdateTypeAsync(ProductType type, CancellationToken ct = default);
    Task UpdateModelAsync(ProductModel model, CancellationToken ct = default);
}
