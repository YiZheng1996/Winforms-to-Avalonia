using XXX.TestBench.Core.Domain.Recipes;

namespace XXX.TestBench.Core.Ports;

public interface IRecipeRepository
{
    Task<RecipeVersion?> GetAsync(int id, CancellationToken ct = default);
    Task<RecipeVersion?> GetLatestAsync(int productModelId, CancellationToken ct = default);
    Task<IReadOnlyList<RecipeVersion>> ListByModelAsync(int productModelId, bool includeRetired, CancellationToken ct = default);
    Task<RecipeVersion?> GetPublishedByModelAsync(int productModelId, CancellationToken ct = default);
    Task<IReadOnlyList<RecipeItem>> ListItemsAsync(int recipeVersionId, CancellationToken ct = default);
    Task<IReadOnlyList<RecipeParameterValue>> ListParameterValuesAsync(int recipeVersionId, CancellationToken ct = default);
    Task AddAsync(RecipeVersion recipe, CancellationToken ct = default);
    Task UpdateAsync(RecipeVersion recipe, CancellationToken ct = default);
    Task AddItemAsync(RecipeItem item, CancellationToken ct = default);
    Task UpdateItemAsync(RecipeItem item, CancellationToken ct = default);
    Task DeleteItemAsync(int itemId, CancellationToken ct = default);
    Task AddParameterValueAsync(RecipeParameterValue value, CancellationToken ct = default);
    /// <summary>
    /// 按项点+参数替换参数值（upsert），用于草稿编辑。
    /// </summary>
    Task ReplaceParameterValueAsync(int recipeItemId, int parameterDefinitionId, string rawValue, CancellationToken ct = default);
}
