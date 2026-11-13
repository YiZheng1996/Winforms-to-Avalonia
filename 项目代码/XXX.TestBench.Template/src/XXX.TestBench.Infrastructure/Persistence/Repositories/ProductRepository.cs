using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 产品类型与型号的数据库实现。
/// </summary>
public sealed class ProductRepository : SqliteRepositoryBase, IProductRepository
{
    /// <summary>
    /// 数据库连接工厂，用于跨多条语句的删除事务。
    /// </summary>
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>
    /// 创建产品仓库。
    /// </summary>
    public ProductRepository(ISqliteConnectionFactory factory) : base(factory) => _factory = factory;

    /// <summary>
    /// 按编号读取产品类型。
    /// </summary>
    public async Task<ProductType?> GetTypeAsync(int id, CancellationToken ct = default)
    {
        var row = await RunDbAsync(() => Select<SqliteProductType>()
            .Where(x => x.Id == id)
            .ToOne(), ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 按编号读取产品型号。
    /// </summary>
    public async Task<ProductModel?> GetModelAsync(int id, CancellationToken ct = default)
    {
        var row = await RunDbAsync(() => Select<SqliteProductModel>()
            .Where(x => x.Id == id)
            .ToOne(), ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 读取产品类型列表。
    /// </summary>
    public async Task<IReadOnlyList<ProductType>> ListTypesAsync(bool includeDisabled, CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() =>
        {
            var query = Select<SqliteProductType>();
            if (!includeDisabled)
                query = query.Where(x => x.IsEnabled == 1);
            return query
                .OrderByDescending(x => x.CreatedAtUtc)
                .OrderByDescending(x => x.Id)
                .ToList();
        }, ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 读取产品型号列表，可按类型筛选。
    /// </summary>
    public async Task<IReadOnlyList<ProductModel>> ListModelsAsync(int? productTypeId, bool includeDisabled, CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() =>
        {
            var query = Select<SqliteProductModel>();
            if (productTypeId is int typeId)
                query = query.Where(x => x.ProductTypeId == typeId);
            if (!includeDisabled)
                query = query.Where(x => x.IsEnabled == 1);
            return query
                .OrderByDescending(x => x.CreatedAtUtc)
                .OrderByDescending(x => x.Id)
                .ToList();
        }, ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 新增产品类型并回填编号。
    /// </summary>
    public async Task AddTypeAsync(ProductType type, CancellationToken ct = default)
    {
        var id = await InsertIdentityAsync(new SqliteProductType
        {
            Code = CompatibilityCode("type"),
            Name = type.Name,
            IsEnabled = type.IsEnabled ? 1 : 0,
            CreatedAtUtc = type.CreatedAtUtc.ToString("O")
        }, ct);
        type.Id = checked((int)id);
    }

    /// <summary>
    /// 新增产品型号并回填编号。
    /// </summary>
    public async Task AddModelAsync(ProductModel model, CancellationToken ct = default)
    {
        var id = await InsertIdentityAsync(new SqliteProductModel
        {
            ProductTypeId = model.ProductTypeId,
            Code = CompatibilityCode("model"),
            Name = model.Name,
            IsEnabled = model.IsEnabled ? 1 : 0,
            CreatedAtUtc = model.CreatedAtUtc.ToString("O")
        }, ct);
        model.Id = checked((int)id);
    }

    /// <summary>
    /// 更新产品类型。
    /// </summary>
    public Task UpdateTypeAsync(ProductType type, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            Update<SqliteProductType>()
                .Where(x => x.Id == type.Id)
                .Set(x => x.Name, type.Name)
                .Set(x => x.IsEnabled, type.IsEnabled ? 1 : 0)
                .ExecuteAffrows();
        }, ct);

    /// <summary>
    /// 更新产品型号。
    /// </summary>
    public Task UpdateModelAsync(ProductModel model, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            Update<SqliteProductModel>()
                .Where(x => x.Id == model.Id)
                .Set(x => x.Name, model.Name)
                .Set(x => x.IsEnabled, model.IsEnabled ? 1 : 0)
                .ExecuteAffrows();
        }, ct);

    /// <summary>
    /// 统计某类型下的型号数量。
    /// </summary>
    public Task<int> CountModelsByTypeAsync(int productTypeId, CancellationToken ct = default)
        => RunDbAsync(() => checked((int)Select<SqliteProductModel>()
            .Where(x => x.ProductTypeId == productTypeId)
            .Count()), ct);

    /// <summary>
    /// 统计某类型关联的试验项点数量。
    /// </summary>
    public Task<int> CountTestPointsByTypeAsync(int productTypeId, CancellationToken ct = default)
        => RunDbAsync(() => checked((int)Select<SqliteTestItemPoint>()
            .Where(x => x.ProductTypeId == productTypeId)
            .Count()), ct);

    /// <summary>
    /// 统计某型号关联的试验记录数量。
    /// </summary>
    public Task<int> CountRecordsByModelAsync(int productModelId, CancellationToken ct = default)
        => RunDbAsync(() => checked((int)Select<SqliteTestRecord>()
            .Where(x => x.ProductModelId == productModelId)
            .Count()), ct);

    /// <summary>
    /// 删除产品类型。
    /// </summary>
    public async Task DeleteTypeAsync(int productTypeId, CancellationToken ct = default)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        await using var txn = await lease.Connection.BeginTransactionAsync(ct);
        try
        {
            await RunDbAsync(() => Delete<SqliteProductType>(lease.Connection, txn)
                .Where(x => x.Id == productTypeId)
                .ExecuteAffrows(), ct);
            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// 删除产品型号并清理产品参数与项点配置。
    /// </summary>
    public async Task DeleteModelAsync(int productModelId, CancellationToken ct = default)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        await using var txn = await lease.Connection.BeginTransactionAsync(ct);
        try
        {
            await RunDbAsync(() => Delete<SqliteModelPointConfig>(lease.Connection, txn)
                .Where(x => x.ProductModelId == productModelId)
                .ExecuteAffrows(), ct);
            await RunDbAsync(() => Delete<SqliteProductTestParameter>(lease.Connection, txn)
                .Where(x => x.ProductModelId == productModelId)
                .ExecuteAffrows(), ct);
            await RunDbAsync(() => Delete<SqliteProductModel>(lease.Connection, txn)
                .Where(x => x.Id == productModelId)
                .ExecuteAffrows(), ct);
            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    private static ProductType Map(SqliteProductType row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        IsEnabled = row.IsEnabled != 0,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
    };

    private static ProductModel Map(SqliteProductModel row) => new()
    {
        Id = row.Id,
        ProductTypeId = row.ProductTypeId,
        Name = row.Name,
        IsEnabled = row.IsEnabled != 0,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
    };

    /// <summary>
    /// 为旧数据生成唯一兼容编码，满足旧表非空约束且不与新编码冲突。
    /// </summary>
    private static string CompatibilityCode(string kind) => $"__legacy_{kind}_{Guid.NewGuid():N}";
}
