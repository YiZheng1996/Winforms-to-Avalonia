using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository : SqliteRepositoryBase, IProductRepository
{
    public ProductRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<ProductType?> GetTypeByCodeAsync(string code, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ProductTypeRow>(
            "SELECT id AS Id, code AS Code, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_types WHERE code=@code",
            new { code }, ct);
        return row is null ? null : Map(row);
    }

    public async Task<ProductType?> GetTypeAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ProductTypeRow>(
            "SELECT id AS Id, code AS Code, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_types WHERE id=@id",
            new { id }, ct);
        return row is null ? null : Map(row);
    }

    public async Task<ProductModel?> GetModelAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ProductModelRow>(
            "SELECT id AS Id, product_type_id AS ProductTypeId, code AS Code, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_models WHERE id=@id",
            new { id }, ct);
        return row is null ? null : Map(row);
    }

    public async Task<ProductModel?> GetModelByCodeAsync(int productTypeId, string code, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ProductModelRow>(
            "SELECT id AS Id, product_type_id AS ProductTypeId, code AS Code, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_models WHERE product_type_id=@productTypeId AND code=@code",
            new { productTypeId, code }, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<ProductType>> ListTypesAsync(bool includeDisabled, CancellationToken ct = default)
    {
        var sql = includeDisabled
            ? "SELECT id AS Id, code AS Code, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_types ORDER BY code"
            : "SELECT id AS Id, code AS Code, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_types WHERE is_enabled=1 ORDER BY code";
        var rows = await QueryAsync<ProductTypeRow>(sql, null, ct);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ProductModel>> ListModelsAsync(int? productTypeId, bool includeDisabled, CancellationToken ct = default)
    {
        var filters = new List<string>();
        if (productTypeId is not null) filters.Add("product_type_id=@productTypeId");
        if (!includeDisabled) filters.Add("is_enabled=1");
        var where = filters.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", filters)}";
        var rows = await QueryAsync<ProductModelRow>(
            $"SELECT id AS Id, product_type_id AS ProductTypeId, code AS Code, name AS Name, is_enabled AS IsEnabled, created_at_utc AS CreatedAtUtc FROM product_models{where} ORDER BY code",
            new { productTypeId }, ct);
        return rows.Select(Map).ToList();
    }

    public async Task AddTypeAsync(ProductType type, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync(
            "INSERT INTO product_types (code, name, is_enabled, created_at_utc) VALUES (@code, @name, @enabled, @created)",
            new { code = type.Code, name = type.Name, enabled = type.IsEnabled ? 1 : 0, created = type.CreatedAtUtc.ToString("O") }, ct);
        type.Id = (int)id;
    }

    public async Task AddModelAsync(ProductModel model, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync(
            "INSERT INTO product_models (product_type_id, code, name, is_enabled, created_at_utc) VALUES (@productTypeId, @code, @name, @enabled, @created)",
            new { productTypeId = model.ProductTypeId, code = model.Code, name = model.Name, enabled = model.IsEnabled ? 1 : 0, created = model.CreatedAtUtc.ToString("O") }, ct);
        model.Id = (int)id;
    }

    public Task UpdateTypeAsync(ProductType type, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE product_types SET name=@name, is_enabled=@enabled WHERE id=@id",
        new { name = type.Name, enabled = type.IsEnabled ? 1 : 0, id = type.Id }, ct);

    public Task UpdateModelAsync(ProductModel model, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE product_models SET name=@name, is_enabled=@enabled WHERE id=@id",
        new { name = model.Name, enabled = model.IsEnabled ? 1 : 0, id = model.Id }, ct);

    private static ProductType Map(ProductTypeRow row) => new()
    {
        Id = row.Id,
        Code = row.Code,
        Name = row.Name,
        IsEnabled = row.IsEnabled != 0,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
    };

    private static ProductModel Map(ProductModelRow row) => new()
    {
        Id = row.Id,
        ProductTypeId = row.ProductTypeId,
        Code = row.Code,
        Name = row.Name,
        IsEnabled = row.IsEnabled != 0,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
    };

    private sealed class ProductTypeRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int IsEnabled { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class ProductModelRow
    {
        public int Id { get; set; }
        public int ProductTypeId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int IsEnabled { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
    }
}
