using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public sealed class RecipeRepository : SqliteRepositoryBase, IRecipeRepository
{
    public RecipeRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<RecipeVersion?> GetAsync(int id, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, product_model_id, version, status, name, report_template_path, created_by_user_id, created_at_utc, published_by_user_id, published_at_utc, retired_by_user_id, retired_at_utc FROM recipe_versions WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<RecipeVersion?> GetLatestAsync(int productModelId, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, product_model_id, version, status, name, report_template_path, created_by_user_id, created_at_utc, published_by_user_id, published_at_utc, retired_by_user_id, retired_at_utc FROM recipe_versions WHERE product_model_id=$pm ORDER BY version DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$pm", productModelId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<RecipeVersion>> ListByModelAsync(int productModelId, bool includeRetired, CancellationToken ct = default)
    {
        var result = new List<RecipeVersion>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = includeRetired
                ? "SELECT id, product_model_id, version, status, name, report_template_path, created_by_user_id, created_at_utc, published_by_user_id, published_at_utc, retired_by_user_id, retired_at_utc FROM recipe_versions WHERE product_model_id=$pm ORDER BY version"
                : "SELECT id, product_model_id, version, status, name, report_template_path, created_by_user_id, created_at_utc, published_by_user_id, published_at_utc, retired_by_user_id, retired_at_utc FROM recipe_versions WHERE product_model_id=$pm AND status!=2 ORDER BY version";
            cmd.Parameters.AddWithValue("$pm", productModelId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(Map(reader));
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<RecipeVersion?> GetPublishedByModelAsync(int productModelId, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, product_model_id, version, status, name, report_template_path, created_by_user_id, created_at_utc, published_by_user_id, published_at_utc, retired_by_user_id, retired_at_utc FROM recipe_versions WHERE product_model_id=$pm AND status=1 ORDER BY version DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$pm", productModelId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<RecipeItem>> ListItemsAsync(int recipeVersionId, CancellationToken ct = default)
    {
        var result = new List<RecipeItem>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, recipe_version_id, test_item_definition_id, sort_order, is_enabled FROM recipe_items WHERE recipe_version_id=$id ORDER BY sort_order";
            cmd.Parameters.AddWithValue("$id", recipeVersionId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(new RecipeItem { Id = reader.GetInt32(0), RecipeVersionId = reader.GetInt32(1), TestItemDefinitionId = reader.GetInt32(2), SortOrder = reader.GetInt32(3), IsEnabled = reader.GetInt32(4) != 0 });
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<RecipeParameterValue>> ListParameterValuesAsync(int recipeVersionId, CancellationToken ct = default)
    {
        var result = new List<RecipeParameterValue>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT pv.id, pv.recipe_item_id, pv.parameter_definition_id, pv.raw_value
                FROM recipe_parameter_values pv
                JOIN recipe_items ri ON ri.id = pv.recipe_item_id
                WHERE ri.recipe_version_id=$id ORDER BY pv.id
                """;
            cmd.Parameters.AddWithValue("$id", recipeVersionId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(new RecipeParameterValue { Id = reader.GetInt32(0), RecipeItemId = reader.GetInt32(1), ParameterDefinitionId = reader.GetInt32(2), RawValue = reader.GetString(3) });
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddAsync(RecipeVersion recipe, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO recipe_versions (product_model_id, version, status, name, report_template_path, created_by_user_id, created_at_utc, published_by_user_id, published_at_utc, retired_by_user_id, retired_at_utc)
                VALUES ($pm, $ver, $status, $name, $template, $creator, $created, $pubBy, $pubAt, $retBy, $retAt)
                """;
            cmd.Parameters.AddWithValue("$pm", recipe.ProductModelId);
            cmd.Parameters.AddWithValue("$ver", recipe.Version);
            cmd.Parameters.AddWithValue("$status", (int)recipe.Status);
            cmd.Parameters.AddWithValue("$name", recipe.Name);
            cmd.Parameters.AddWithValue("$template", (object?)recipe.ReportTemplatePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$creator", recipe.CreatedByUserId);
            cmd.Parameters.AddWithValue("$created", recipe.CreatedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$pubBy", (object?)recipe.PublishedByUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pubAt", (object?)recipe.PublishedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$retBy", (object?)recipe.RetiredByUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$retAt", (object?)recipe.RetiredAtUtc?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
            recipe.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateAsync(RecipeVersion recipe, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE recipe_versions SET status=$status, name=$name, report_template_path=$template,
                published_by_user_id=$pubBy, published_at_utc=$pubAt, retired_by_user_id=$retBy, retired_at_utc=$retAt WHERE id=$id
                """;
            cmd.Parameters.AddWithValue("$id", recipe.Id);
            cmd.Parameters.AddWithValue("$status", (int)recipe.Status);
            cmd.Parameters.AddWithValue("$name", recipe.Name);
            cmd.Parameters.AddWithValue("$template", (object?)recipe.ReportTemplatePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pubBy", (object?)recipe.PublishedByUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pubAt", (object?)recipe.PublishedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$retBy", (object?)recipe.RetiredByUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$retAt", (object?)recipe.RetiredAtUtc?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddItemAsync(RecipeItem item, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO recipe_items (recipe_version_id, test_item_definition_id, sort_order, is_enabled) VALUES ($rv, $tid, $sort, $enabled)";
            cmd.Parameters.AddWithValue("$rv", item.RecipeVersionId);
            cmd.Parameters.AddWithValue("$tid", item.TestItemDefinitionId);
            cmd.Parameters.AddWithValue("$sort", item.SortOrder);
            cmd.Parameters.AddWithValue("$enabled", item.IsEnabled ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct);
            item.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddParameterValueAsync(RecipeParameterValue value, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO recipe_parameter_values (recipe_item_id, parameter_definition_id, raw_value) VALUES ($ri, $pd, $raw)";
            cmd.Parameters.AddWithValue("$ri", value.RecipeItemId);
            cmd.Parameters.AddWithValue("$pd", value.ParameterDefinitionId);
            cmd.Parameters.AddWithValue("$raw", value.RawValue);
            await cmd.ExecuteNonQueryAsync(ct);
            value.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateItemAsync(RecipeItem item, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE recipe_items SET sort_order=$sort, is_enabled=$enabled WHERE id=$id";
            cmd.Parameters.AddWithValue("$sort", item.SortOrder);
            cmd.Parameters.AddWithValue("$enabled", item.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", item.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task DeleteItemAsync(int itemId, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM recipe_parameter_values WHERE recipe_item_id=$id";
                cmd.Parameters.AddWithValue("$id", itemId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM recipe_items WHERE id=$id";
                cmd.Parameters.AddWithValue("$id", itemId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task ReplaceParameterValueAsync(int recipeItemId, int parameterDefinitionId, string rawValue, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO recipe_parameter_values (recipe_item_id, parameter_definition_id, raw_value)
                VALUES ($ri, $pd, $raw)
                ON CONFLICT(recipe_item_id, parameter_definition_id) DO UPDATE SET raw_value=$raw
                """;
            cmd.Parameters.AddWithValue("$ri", recipeItemId);
            cmd.Parameters.AddWithValue("$pd", parameterDefinitionId);
            cmd.Parameters.AddWithValue("$raw", rawValue);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }
    private static RecipeVersion Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        ProductModelId = r.GetInt32(1),
        Version = r.GetInt32(2),
        Status = (RecipeStatus)r.GetInt32(3),
        Name = r.GetString(4),
        ReportTemplatePath = ParseNullableString(r.GetValue(5)),
        CreatedByUserId = r.GetInt32(6),
        CreatedAtUtc = ParseUtc(r.GetString(7)),
        PublishedByUserId = r.IsDBNull(8) ? null : r.GetInt32(8),
        PublishedAtUtc = ParseNullableUtc(r.GetValue(9)),
        RetiredByUserId = r.IsDBNull(10) ? null : r.GetInt32(10),
        RetiredAtUtc = ParseNullableUtc(r.GetValue(11))
    };
}
