using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public sealed class RecipeRepository : SqliteRepositoryBase, IRecipeRepository
{
    public RecipeRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<RecipeVersion?> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<RecipeVersionRow>(
            RecipeVersionSelect + " WHERE id=@id", new { id }, ct);
        return row is null ? null : Map(row);
    }

    public async Task<RecipeVersion?> GetLatestAsync(int productModelId, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<RecipeVersionRow>(
            RecipeVersionSelect + " WHERE product_model_id=@productModelId ORDER BY version DESC LIMIT 1",
            new { productModelId }, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<RecipeVersion>> ListByModelAsync(int productModelId, bool includeRetired, CancellationToken ct = default)
    {
        var statusFilter = includeRetired ? string.Empty : " AND status!=2";
        var rows = await QueryAsync<RecipeVersionRow>(
            RecipeVersionSelect + $" WHERE product_model_id=@productModelId{statusFilter} ORDER BY version",
            new { productModelId }, ct);
        return rows.Select(Map).ToList();
    }

    public async Task<RecipeVersion?> GetPublishedByModelAsync(int productModelId, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<RecipeVersionRow>(
            RecipeVersionSelect + " WHERE product_model_id=@productModelId AND status=1 ORDER BY version DESC LIMIT 1",
            new { productModelId }, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<RecipeItem>> ListItemsAsync(int recipeVersionId, CancellationToken ct = default)
    {
        var rows = await QueryAsync<RecipeItemRow>(
            "SELECT id AS Id, recipe_version_id AS RecipeVersionId, test_item_definition_id AS TestItemDefinitionId, sort_order AS SortOrder, is_enabled AS IsEnabled FROM recipe_items WHERE recipe_version_id=@recipeVersionId ORDER BY sort_order",
            new { recipeVersionId }, ct);
        return rows.Select(row => new RecipeItem
        {
            Id = row.Id,
            RecipeVersionId = row.RecipeVersionId,
            TestItemDefinitionId = row.TestItemDefinitionId,
            SortOrder = row.SortOrder,
            IsEnabled = row.IsEnabled != 0
        }).ToList();
    }

    public async Task<IReadOnlyList<RecipeParameterValue>> ListParameterValuesAsync(int recipeVersionId, CancellationToken ct = default)
    {
        var rows = await QueryAsync<RecipeParameterValueRow>("""
            SELECT pv.id AS Id, pv.recipe_item_id AS RecipeItemId, pv.parameter_definition_id AS ParameterDefinitionId, pv.raw_value AS RawValue
            FROM recipe_parameter_values pv
            JOIN recipe_items ri ON ri.id = pv.recipe_item_id
            WHERE ri.recipe_version_id=@recipeVersionId ORDER BY pv.id
            """, new { recipeVersionId }, ct);
        return rows.Select(row => new RecipeParameterValue
        {
            Id = row.Id,
            RecipeItemId = row.RecipeItemId,
            ParameterDefinitionId = row.ParameterDefinitionId,
            RawValue = row.RawValue
        }).ToList();
    }

    public async Task AddAsync(RecipeVersion recipe, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO recipe_versions (product_model_id, version, status, name, report_template_path, created_by_user_id, created_at_utc, published_by_user_id, published_at_utc, retired_by_user_id, retired_at_utc)
            VALUES (@productModelId, @version, @status, @name, @template, @createdByUserId, @createdAtUtc, @publishedByUserId, @publishedAtUtc, @retiredByUserId, @retiredAtUtc)
            """, new
        {
            productModelId = recipe.ProductModelId,
            version = recipe.Version,
            status = (int)recipe.Status,
            name = recipe.Name,
            template = DbValue(recipe.ReportTemplatePath),
            createdByUserId = recipe.CreatedByUserId,
            createdAtUtc = recipe.CreatedAtUtc.ToString("O"),
            publishedByUserId = DbValue(recipe.PublishedByUserId),
            publishedAtUtc = DbValue(recipe.PublishedAtUtc?.ToString("O")),
            retiredByUserId = DbValue(recipe.RetiredByUserId),
            retiredAtUtc = DbValue(recipe.RetiredAtUtc?.ToString("O"))
        }, ct);
        recipe.Id = (int)id;
    }

    public Task UpdateAsync(RecipeVersion recipe, CancellationToken ct = default) => ExecuteAsync("""
        UPDATE recipe_versions SET status=@status, name=@name, report_template_path=@template,
        published_by_user_id=@publishedByUserId, published_at_utc=@publishedAtUtc, retired_by_user_id=@retiredByUserId, retired_at_utc=@retiredAtUtc WHERE id=@id
        """, new
    {
        id = recipe.Id,
        status = (int)recipe.Status,
        name = recipe.Name,
        template = DbValue(recipe.ReportTemplatePath),
        publishedByUserId = DbValue(recipe.PublishedByUserId),
        publishedAtUtc = DbValue(recipe.PublishedAtUtc?.ToString("O")),
        retiredByUserId = DbValue(recipe.RetiredByUserId),
        retiredAtUtc = DbValue(recipe.RetiredAtUtc?.ToString("O"))
    }, ct);

    public async Task AddItemAsync(RecipeItem item, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync(
            "INSERT INTO recipe_items (recipe_version_id, test_item_definition_id, sort_order, is_enabled) VALUES (@recipeVersionId, @testItemDefinitionId, @sortOrder, @enabled)",
            new { recipeVersionId = item.RecipeVersionId, testItemDefinitionId = item.TestItemDefinitionId, sortOrder = item.SortOrder, enabled = item.IsEnabled ? 1 : 0 }, ct);
        item.Id = (int)id;
    }

    public async Task AddParameterValueAsync(RecipeParameterValue value, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync(
            "INSERT INTO recipe_parameter_values (recipe_item_id, parameter_definition_id, raw_value) VALUES (@recipeItemId, @parameterDefinitionId, @rawValue)",
            new { recipeItemId = value.RecipeItemId, parameterDefinitionId = value.ParameterDefinitionId, rawValue = value.RawValue }, ct);
        value.Id = (int)id;
    }

    public Task UpdateItemAsync(RecipeItem item, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE recipe_items SET sort_order=@sortOrder, is_enabled=@enabled WHERE id=@id",
        new { sortOrder = item.SortOrder, enabled = item.IsEnabled ? 1 : 0, id = item.Id }, ct);

    public async Task DeleteItemAsync(int itemId, CancellationToken ct = default)
    {
        await ExecuteAsync("DELETE FROM recipe_parameter_values WHERE recipe_item_id=@itemId", new { itemId }, ct);
        await ExecuteAsync("DELETE FROM recipe_items WHERE id=@itemId", new { itemId }, ct);
    }

    public Task ReplaceParameterValueAsync(int recipeItemId, int parameterDefinitionId, string rawValue, CancellationToken ct = default) => ExecuteAsync("""
        INSERT INTO recipe_parameter_values (recipe_item_id, parameter_definition_id, raw_value)
        VALUES (@recipeItemId, @parameterDefinitionId, @rawValue)
        ON CONFLICT(recipe_item_id, parameter_definition_id) DO UPDATE SET raw_value=@rawValue
        """, new { recipeItemId, parameterDefinitionId, rawValue }, ct);

    private static RecipeVersion Map(RecipeVersionRow row) => new()
    {
        Id = row.Id,
        ProductModelId = row.ProductModelId,
        Version = row.Version,
        Status = (RecipeStatus)row.Status,
        Name = row.Name,
        ReportTemplatePath = row.ReportTemplatePath,
        CreatedByUserId = row.CreatedByUserId,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc),
        PublishedByUserId = row.PublishedByUserId,
        PublishedAtUtc = ParseNullableUtc(row.PublishedAtUtc),
        RetiredByUserId = row.RetiredByUserId,
        RetiredAtUtc = ParseNullableUtc(row.RetiredAtUtc)
    };

    private const string RecipeVersionSelect = "SELECT id AS Id, product_model_id AS ProductModelId, version AS Version, status AS Status, name AS Name, report_template_path AS ReportTemplatePath, created_by_user_id AS CreatedByUserId, created_at_utc AS CreatedAtUtc, published_by_user_id AS PublishedByUserId, published_at_utc AS PublishedAtUtc, retired_by_user_id AS RetiredByUserId, retired_at_utc AS RetiredAtUtc FROM recipe_versions";

    private sealed class RecipeVersionRow
    {
        public int Id { get; set; }
        public int ProductModelId { get; set; }
        public int Version { get; set; }
        public int Status { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ReportTemplatePath { get; set; }
        public int CreatedByUserId { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public int? PublishedByUserId { get; set; }
        public string? PublishedAtUtc { get; set; }
        public int? RetiredByUserId { get; set; }
        public string? RetiredAtUtc { get; set; }
    }

    private sealed class RecipeItemRow
    {
        public int Id { get; set; }
        public int RecipeVersionId { get; set; }
        public int TestItemDefinitionId { get; set; }
        public int SortOrder { get; set; }
        public int IsEnabled { get; set; }
    }

    private sealed class RecipeParameterValueRow
    {
        public int Id { get; set; }
        public int RecipeItemId { get; set; }
        public int ParameterDefinitionId { get; set; }
        public string RawValue { get; set; } = string.Empty;
    }
}
