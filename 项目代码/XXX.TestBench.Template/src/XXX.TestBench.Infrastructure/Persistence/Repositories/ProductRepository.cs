using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 产品类型与型号的数据库实现。
/// </summary>
public sealed class ProductRepository : SqliteRepositoryBase, IProductRepository
{
    /// <summary>
    /// 数据库连接工厂。
    /// </summary>
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>
    /// 创建产品仓库。
    /// </summary>
    public ProductRepository(ISqliteConnectionFactory factory) : base(factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 按编号读取产品类型。
    /// </summary>
    public async Task<ProductType?> GetTypeAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ProductTypeRow>(
            "SELECT id AS Id, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_types WHERE id=@id",
            new { id }, ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 按编号读取产品型号。
    /// </summary>
    public async Task<ProductModel?> GetModelAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ProductModelRow>(
            "SELECT id AS Id, product_type_id AS ProductTypeId, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_models WHERE id=@id",
            new { id }, ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 读取产品类型列表。
    /// </summary>
    public async Task<IReadOnlyList<ProductType>> ListTypesAsync(bool includeDisabled, CancellationToken ct = default)
    {
        var sql = includeDisabled
            ? "SELECT id AS Id, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_types ORDER BY created_at_utc DESC, id DESC"
            : "SELECT id AS Id, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_types WHERE is_enabled=1 ORDER BY created_at_utc DESC, id DESC";
        var rows = await QueryAsync<ProductTypeRow>(sql, null, ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 读取产品型号列表，可按类型筛选。
    /// </summary>
    public async Task<IReadOnlyList<ProductModel>> ListModelsAsync(int? productTypeId, bool includeDisabled, CancellationToken ct = default)
    {
        var filters = new List<string>();
        if (productTypeId is not null) filters.Add("product_type_id=@productTypeId");
        if (!includeDisabled) filters.Add("is_enabled=1");
        var where = filters.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", filters)}";
        var rows = await QueryAsync<ProductModelRow>(
            $"SELECT id AS Id, product_type_id AS ProductTypeId, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_models{where} ORDER BY created_at_utc DESC, id DESC",
            new { productTypeId }, ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 新增产品类型并回填编号。
    /// </summary>
    public async Task AddTypeAsync(ProductType type, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync(
            "INSERT INTO product_types (code, name, is_enabled, created_at_utc) VALUES (@code, @name, @enabled, @created)",
            new { code = CompatibilityCode("type"), name = type.Name, enabled = type.IsEnabled ? 1 : 0, created = type.CreatedAtUtc.ToString("O") }, ct);
        type.Id = (int)id;
    }

    /// <summary>
    /// 新增产品型号并回填编号。
    /// </summary>
    public async Task AddModelAsync(ProductModel model, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync(
            "INSERT INTO product_models (product_type_id, code, name, is_enabled, created_at_utc) VALUES (@productTypeId, @code, @name, @enabled, @created)",
            new { productTypeId = model.ProductTypeId, code = CompatibilityCode("model"), name = model.Name, enabled = model.IsEnabled ? 1 : 0, created = model.CreatedAtUtc.ToString("O") }, ct);
        model.Id = (int)id;
    }

    /// <summary>
    /// 更新产品类型。
    /// </summary>
    public Task UpdateTypeAsync(ProductType type, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE product_types SET name=@name, is_enabled=@enabled WHERE id=@id",
        new { name = type.Name, enabled = type.IsEnabled ? 1 : 0, id = type.Id }, ct);

    /// <summary>
    /// 更新产品型号。
    /// </summary>
    public Task UpdateModelAsync(ProductModel model, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE product_models SET name=@name, is_enabled=@enabled WHERE id=@id",
        new { name = model.Name, enabled = model.IsEnabled ? 1 : 0, id = model.Id }, ct);

    /// <summary>
    /// 统计某类型下的型号数量。
    /// </summary>
    public async Task<int> CountModelsByTypeAsync(int productTypeId, CancellationToken ct = default)
    {
        var value = await ScalarAsync(
            "SELECT COUNT(1) FROM product_models WHERE product_type_id=@productTypeId",
            new { productTypeId }, ct);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 统计某类型关联的试验项点数量。
    /// </summary>
    public async Task<int> CountTestPointsByTypeAsync(int productTypeId, CancellationToken ct = default)
    {
        var value = await ScalarAsync(
            "SELECT COUNT(1) FROM test_item_points WHERE product_type_id=@productTypeId",
            new { productTypeId }, ct);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 统计某型号关联的试验记录数量。
    /// </summary>
    public async Task<int> CountRecordsByModelAsync(int productModelId, CancellationToken ct = default)
    {
        var value = await ScalarAsync(
            "SELECT COUNT(1) FROM test_records WHERE product_model_id=@productModelId",
            new { productModelId }, ct);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 删除产品类型并清理类型级参数。
    /// </summary>
    public async Task DeleteTypeAsync(int productTypeId, CancellationToken ct = default)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        await using var txn = await lease.Connection.BeginTransactionAsync(ct);
        try
        {
            await Db.Ado.ExecuteNonQueryAsync(lease.Connection, txn,
                "DELETE FROM product_type_test_parameters WHERE product_type_id=@id",
                new { id = productTypeId }, ct);
            await Db.Ado.ExecuteNonQueryAsync(lease.Connection, txn,
                "DELETE FROM product_types WHERE id=@id",
                new { id = productTypeId }, ct);
            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// 删除产品型号并清理型号级参数与项点配置。
    /// </summary>
    public async Task DeleteModelAsync(int productModelId, CancellationToken ct = default)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        await using var txn = await lease.Connection.BeginTransactionAsync(ct);
        try
        {
            await Db.Ado.ExecuteNonQueryAsync(lease.Connection, txn,
                "DELETE FROM model_point_configs WHERE product_model_id=@id",
                new { id = productModelId }, ct);
            await Db.Ado.ExecuteNonQueryAsync(lease.Connection, txn,
                "DELETE FROM product_model_test_parameters WHERE product_model_id=@id",
                new { id = productModelId }, ct);
            await Db.Ado.ExecuteNonQueryAsync(lease.Connection, txn,
                "DELETE FROM product_models WHERE id=@id",
                new { id = productModelId }, ct);
            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// 把查询结果转换为产品类型对象。
    /// </summary>
    private static ProductType Map(ProductTypeRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        IsEnabled = row.IsEnabled != 0,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
    };

    /// <summary>
    /// 把查询结果转换为产品型号对象。
    /// </summary>
    private static ProductModel Map(ProductModelRow row) => new()
    {
        Id = row.Id,
        ProductTypeId = row.ProductTypeId,
        Name = row.Name,
        IsEnabled = row.IsEnabled != 0,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
    };

    private sealed class ProductTypeRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int IsEnabled { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class ProductModelRow
    {
        public int Id { get; set; }
        public int ProductTypeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int IsEnabled { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
    }

    /// <summary>
    /// 为旧数据生成唯一兼容编码，满足旧表非空约束且不与新编码冲突。
    /// </summary>
    private static string CompatibilityCode(string kind) => $"__legacy_{kind}_{Guid.NewGuid():N}";
}
