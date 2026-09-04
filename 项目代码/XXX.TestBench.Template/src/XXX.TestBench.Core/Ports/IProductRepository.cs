using XXX.TestBench.Core.Domain.Products;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 产品类型与型号的数据仓库接口。
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// 按编号读取产品类型。
    /// </summary>
    Task<ProductType?> GetTypeAsync(int id, CancellationToken ct = default);
    /// <summary>
    /// 按编号读取产品型号。
    /// </summary>
    Task<ProductModel?> GetModelAsync(int id, CancellationToken ct = default);
    /// <summary>
    /// 读取产品类型列表。
    /// </summary>
    Task<IReadOnlyList<ProductType>> ListTypesAsync(bool includeDisabled, CancellationToken ct = default);
    /// <summary>
    /// 读取产品型号列表，可按类型筛选。
    /// </summary>
    Task<IReadOnlyList<ProductModel>> ListModelsAsync(int? productTypeId, bool includeDisabled, CancellationToken ct = default);
    /// <summary>
    /// 新增产品类型。
    /// </summary>
    Task AddTypeAsync(ProductType type, CancellationToken ct = default);
    /// <summary>
    /// 新增产品型号。
    /// </summary>
    Task AddModelAsync(ProductModel model, CancellationToken ct = default);
    /// <summary>
    /// 更新产品类型。
    /// </summary>
    Task UpdateTypeAsync(ProductType type, CancellationToken ct = default);
    /// <summary>
    /// 更新产品型号。
    /// </summary>
    Task UpdateModelAsync(ProductModel model, CancellationToken ct = default);
    /// <summary>
    /// 统计某产品类型下的型号数量。
    /// </summary>
    Task<int> CountModelsByTypeAsync(int productTypeId, CancellationToken ct = default);
    /// <summary>
    /// 统计某产品类型关联的试验项点数量。
    /// </summary>
    Task<int> CountTestPointsByTypeAsync(int productTypeId, CancellationToken ct = default);
    /// <summary>
    /// 统计某产品型号关联的试验记录数量。
    /// </summary>
    Task<int> CountRecordsByModelAsync(int productModelId, CancellationToken ct = default);
    /// <summary>
    /// 删除产品类型。
    /// </summary>
    Task DeleteTypeAsync(int productTypeId, CancellationToken ct = default);
    /// <summary>
    /// 删除产品型号。
    /// </summary>
    Task DeleteModelAsync(int productModelId, CancellationToken ct = default);
}
