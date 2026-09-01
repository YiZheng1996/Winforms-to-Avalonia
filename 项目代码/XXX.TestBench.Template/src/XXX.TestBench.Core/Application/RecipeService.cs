using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

public sealed record RecipeDraftSnapshot(RecipeVersion Version, IReadOnlyList<RecipeItem> Items, IReadOnlyList<RecipeParameterValue> Values);

/// <summary>配方：草稿编辑、校验、发布、复制新版本与停用。发布后不可原地修改。</summary>
public sealed class RecipeService
{
    private readonly IRecipeRepository _recipes;
    private readonly IProductRepository _products;
    private readonly ITestDefinitionRepository _definitions;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public RecipeService(IRecipeRepository recipes, IProductRepository products, ITestDefinitionRepository definitions, IClock clock, IAuditLog audit)
    {
        _recipes = recipes;
        _products = products;
        _definitions = definitions;
        _clock = clock;
        _audit = audit;
    }

    public async Task<RecipeVersion> CreateDraftAsync(UserContext actor, int productModelId, string name, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        var model = await _products.GetModelAsync(productModelId, ct) ?? throw new DomainException("产品型号不存在");
        if (!model.IsEnabled) throw new DomainException("产品型号已停用，不能创建配方");
        var latest = await _recipes.GetLatestAsync(productModelId, ct);
        var version = new RecipeVersion
        {
            ProductModelId = productModelId,
            Version = (latest?.Version ?? 0) + 1,
            Name = string.IsNullOrWhiteSpace(name) ? $"配方 V{(latest?.Version ?? 0) + 1}" : name,
            CreatedByUserId = actor.UserId,
            CreatedAtUtc = _clock.UtcNow
        };
        await _recipes.AddAsync(version, ct);
        await _audit.WriteAsync(actor.LoginName, "RecipeDraftCreated", $"recipe:{version.Id}", $"model:{productModelId}", ct);
        return version;
    }

    private async Task<RecipeVersion> RequireDraftAsync(int recipeId, CancellationToken ct)
    {
        var recipe = await _recipes.GetAsync(recipeId, ct) ?? throw new DomainException("配方不存在");
        if (recipe.Status != RecipeStatus.Draft) throw new DomainException("只有草稿配方可以编辑");
        return recipe;
    }

    public async Task RenameDraftAsync(UserContext actor, int recipeId, string name, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("配方名称不能为空");
        var recipe = await RequireDraftAsync(recipeId, ct);
        recipe.Name = name.Trim();
        await _recipes.UpdateAsync(recipe, ct);
        await _audit.WriteAsync(actor.LoginName, "RecipeDraftRenamed", $"recipe:{recipeId}", name, ct);
    }

    public async Task SetReportTemplateAsync(UserContext actor, int recipeId, string templatePath, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        if (string.IsNullOrWhiteSpace(templatePath)) throw new DomainException("报表模板路径不能为空");
        var recipe = await RequireDraftAsync(recipeId, ct);
        recipe.ReportTemplatePath = templatePath.Trim();
        await _recipes.UpdateAsync(recipe, ct);
        await _audit.WriteAsync(actor.LoginName, "RecipeTemplateSet", $"recipe:{recipeId}", templatePath, ct);
    }

    public async Task AddItemAsync(UserContext actor, int recipeId, int itemDefinitionId, int sortOrder, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        var recipe = await RequireDraftAsync(recipeId, ct);
        var itemDef = await _definitions.GetItemAsync(itemDefinitionId, ct) ?? throw new DomainException("试验项定义不存在");
        if (!itemDef.IsEnabled) throw new DomainException("试验项已停用，不能加入配方");

        var items = await _recipes.ListItemsAsync(recipeId, ct);
        if (items.Any(i => i.TestItemDefinitionId == itemDefinitionId))
            throw new DomainException("该试验项已在配方中");
        if (items.Any(i => i.SortOrder == sortOrder))
            throw new DomainException($"顺序 {sortOrder} 已被占用");

        await _recipes.AddItemAsync(new RecipeItem { RecipeVersionId = recipeId, TestItemDefinitionId = itemDefinitionId, SortOrder = sortOrder }, ct);
        await _audit.WriteAsync(actor.LoginName, "RecipeItemAdded", $"recipe:{recipeId}", $"item:{itemDefinitionId}", ct);
    }

    public async Task RemoveItemAsync(UserContext actor, int recipeId, int itemId, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        await RequireDraftAsync(recipeId, ct);
        var items = await _recipes.ListItemsAsync(recipeId, ct);
        if (items.All(i => i.Id != itemId)) throw new DomainException("配方中不存在该项点");
        await _recipes.DeleteItemAsync(itemId, ct);
        await _audit.WriteAsync(actor.LoginName, "RecipeItemRemoved", $"recipe:{recipeId}", $"item:{itemId}", ct);
    }

    public async Task SetItemOrderAsync(UserContext actor, int recipeId, int itemId, int sortOrder, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        await RequireDraftAsync(recipeId, ct);
        var items = await _recipes.ListItemsAsync(recipeId, ct);
        var item = items.FirstOrDefault(i => i.Id == itemId) ?? throw new DomainException("配方中不存在该项点");
        var conflicting = items.FirstOrDefault(i => i.Id != itemId && i.SortOrder == sortOrder);
        if (conflicting is not null)
        {
            conflicting.SortOrder = item.SortOrder;
            await _recipes.UpdateItemAsync(conflicting, ct);
        }
        item.SortOrder = sortOrder;
        await _recipes.UpdateItemAsync(item, ct);
        await _audit.WriteAsync(actor.LoginName, "RecipeItemReordered", $"recipe:{recipeId}", $"item:{itemId}->{sortOrder}", ct);
    }
    public async Task SetParameterValueAsync(UserContext actor, int recipeId, int itemId, int parameterDefinitionId, string rawValue, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        await RequireDraftAsync(recipeId, ct);
        var param = await _definitions.GetParameterAsync(parameterDefinitionId, ct) ?? throw new DomainException("参数定义不存在");
        var error = param.Validate(rawValue);
        if (error is not null) throw new DomainException(error);
        await _recipes.ReplaceParameterValueAsync(itemId, parameterDefinitionId, rawValue, ct);
    }

    public async Task<IReadOnlyList<string>> ValidateDraftAsync(int recipeId, CancellationToken ct = default)
    {
        var recipe = await _recipes.GetAsync(recipeId, ct) ?? throw new DomainException("配方不存在");
        var errors = new List<string>();
        var items = await _recipes.ListItemsAsync(recipeId, ct);
        var enabled = items.Where(i => i.IsEnabled).OrderBy(i => i.SortOrder).ToList();
        if (enabled.Count == 0) { errors.Add("配方没有启用的项点"); return errors; }
        if (enabled.Select(i => i.SortOrder).Distinct().Count() != enabled.Count) errors.Add("项点顺序存在重复");

        var values = await _recipes.ListParameterValuesAsync(recipeId, ct);
        foreach (var item in enabled)
        {
            var definitions = await _definitions.ListParametersAsync(item.TestItemDefinitionId, ct);
            foreach (var def in definitions)
            {
                var value = values.FirstOrDefault(v => v.RecipeItemId == item.Id && v.ParameterDefinitionId == def.Id);
                var error = def.Validate(value?.RawValue);
                if (error is not null) errors.Add($"{recipe.Name} - {def.Name}: {error}");
            }
        }
        return errors;
    }

    public async Task PublishAsync(UserContext actor, int recipeId, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        var recipe = await _recipes.GetAsync(recipeId, ct) ?? throw new DomainException("配方不存在");
        if (recipe.Status != RecipeStatus.Draft) throw new DomainException("只有草稿配方可以发布");
        var errors = await ValidateDraftAsync(recipeId, ct);
        if (errors.Count > 0) throw new DomainException("配方校验未通过：" + string.Join("；", errors));

        recipe.Publish(actor.UserId, _clock.UtcNow);
        await _recipes.UpdateAsync(recipe, ct);
        await _audit.WriteAsync(actor.LoginName, "RecipePublished", $"recipe:{recipeId}", $"version:{recipe.Version}", ct);
    }

    public async Task<RecipeVersion> NewVersionFromAsync(UserContext actor, int recipeId, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        var source = await _recipes.GetAsync(recipeId, ct) ?? throw new DomainException("配方不存在");
        if (source.Status == RecipeStatus.Draft) throw new DomainException("草稿不能复制为新版本");
        var latest = await _recipes.GetLatestAsync(source.ProductModelId, ct);
        var copy = source.CreateNewVersion((latest?.Version ?? source.Version) + 1, actor.UserId, _clock.UtcNow);
        await _recipes.AddAsync(copy, ct);

        foreach (var item in await _recipes.ListItemsAsync(recipeId, ct))
        {
            var newItem = new RecipeItem { RecipeVersionId = copy.Id, TestItemDefinitionId = item.TestItemDefinitionId, SortOrder = item.SortOrder, IsEnabled = item.IsEnabled };
            await _recipes.AddItemAsync(newItem, ct);
            foreach (var value in (await _recipes.ListParameterValuesAsync(recipeId, ct)).Where(v => v.RecipeItemId == item.Id))
            {
                await _recipes.AddParameterValueAsync(new RecipeParameterValue { RecipeItemId = newItem.Id, ParameterDefinitionId = value.ParameterDefinitionId, RawValue = value.RawValue }, ct);
            }
        }
        await _audit.WriteAsync(actor.LoginName, "RecipeVersionCopied", $"recipe:{copy.Id}", $"from:{recipeId}", ct);
        return copy;
    }

    public async Task RetireAsync(UserContext actor, int recipeId, CancellationToken ct = default)
    {
        actor.EnsurePermission(Core.Domain.Identity.PermissionCode.ManageRecipes);
        var recipe = await _recipes.GetAsync(recipeId, ct) ?? throw new DomainException("配方不存在");
        recipe.Retire(actor.UserId, _clock.UtcNow);
        await _recipes.UpdateAsync(recipe, ct);
        await _audit.WriteAsync(actor.LoginName, "RecipeRetired", $"recipe:{recipeId}", null, ct);
    }

    public async Task<RecipeDraftSnapshot> GetDraftAsync(int recipeId, CancellationToken ct = default)
    {
        var recipe = await _recipes.GetAsync(recipeId, ct) ?? throw new DomainException("配方不存在");
        var items = await _recipes.ListItemsAsync(recipeId, ct);
        var values = await _recipes.ListParameterValuesAsync(recipeId, ct);
        return new RecipeDraftSnapshot(recipe, items, values);
    }
}
