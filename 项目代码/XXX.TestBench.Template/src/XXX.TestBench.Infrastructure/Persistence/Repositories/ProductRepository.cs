using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository : SqliteRepositoryBase, IProductRepository
{
    public ProductRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<ProductType?> GetTypeByCodeAsync(string code, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, code, name, is_enabled, created_at_utc FROM product_types WHERE code=$code";
            cmd.Parameters.AddWithValue("$code", code);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? MapType(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<ProductType?> GetTypeAsync(int id, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, code, name, is_enabled, created_at_utc FROM product_types WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? MapType(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<ProductModel?> GetModelAsync(int id, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, product_type_id, code, name, is_enabled, created_at_utc FROM product_models WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? MapModel(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<ProductModel?> GetModelByCodeAsync(int productTypeId, string code, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, product_type_id, code, name, is_enabled, created_at_utc FROM product_models WHERE product_type_id=$pt AND code=$code";
            cmd.Parameters.AddWithValue("$pt", productTypeId);
            cmd.Parameters.AddWithValue("$code", code);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? MapModel(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<ProductType>> ListTypesAsync(bool includeDisabled, CancellationToken ct = default)
    {
        var result = new List<ProductType>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = includeDisabled ? "SELECT id, code, name, is_enabled, created_at_utc FROM product_types ORDER BY code" : "SELECT id, code, name, is_enabled, created_at_utc FROM product_types WHERE is_enabled=1 ORDER BY code";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(MapType(reader));
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<ProductModel>> ListModelsAsync(int? productTypeId, bool includeDisabled, CancellationToken ct = default)
    {
        var result = new List<ProductModel>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = productTypeId is null
                ? "SELECT id, product_type_id, code, name, is_enabled, created_at_utc FROM product_models ORDER BY code"
                : "SELECT id, product_type_id, code, name, is_enabled, created_at_utc FROM product_models WHERE product_type_id=$pt ORDER BY code";
            if (productTypeId is not null) cmd.Parameters.AddWithValue("$pt", productTypeId.Value);
            if (productTypeId is null && !includeDisabled)
                cmd.CommandText = "SELECT id, product_type_id, code, name, is_enabled, created_at_utc FROM product_models WHERE is_enabled=1 ORDER BY code";
            else if (productTypeId is not null && !includeDisabled)
                cmd.CommandText = "SELECT id, product_type_id, code, name, is_enabled, created_at_utc FROM product_models WHERE product_type_id=$pt AND is_enabled=1 ORDER BY code";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(MapModel(reader));
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddTypeAsync(ProductType type, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO product_types (code, name, is_enabled, created_at_utc) VALUES ($code, $name, $enabled, $created)";
            cmd.Parameters.AddWithValue("$code", type.Code);
            cmd.Parameters.AddWithValue("$name", type.Name);
            cmd.Parameters.AddWithValue("$enabled", type.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$created", type.CreatedAtUtc.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
            type.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddModelAsync(ProductModel model, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO product_models (product_type_id, code, name, is_enabled, created_at_utc) VALUES ($pt, $code, $name, $enabled, $created)";
            cmd.Parameters.AddWithValue("$pt", model.ProductTypeId);
            cmd.Parameters.AddWithValue("$code", model.Code);
            cmd.Parameters.AddWithValue("$name", model.Name);
            cmd.Parameters.AddWithValue("$enabled", model.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$created", model.CreatedAtUtc.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
            model.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateTypeAsync(ProductType type, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE product_types SET name=$name, is_enabled=$enabled WHERE id=$id";
            cmd.Parameters.AddWithValue("$name", type.Name);
            cmd.Parameters.AddWithValue("$enabled", type.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", type.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateModelAsync(ProductModel model, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE product_models SET name=$name, is_enabled=$enabled WHERE id=$id";
            cmd.Parameters.AddWithValue("$name", model.Name);
            cmd.Parameters.AddWithValue("$enabled", model.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", model.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }
    private static ProductType MapType(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        IsEnabled = r.GetInt32(3) != 0,
        CreatedAtUtc = ParseUtc(r.GetString(4))
    };

    private static ProductModel MapModel(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        ProductTypeId = r.GetInt32(1),
        Code = r.GetString(2),
        Name = r.GetString(3),
        IsEnabled = r.GetInt32(4) != 0,
        CreatedAtUtc = ParseUtc(r.GetString(5))
    };
}
