using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Domain.TestDefinitions;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class RecipeServiceTests
{
    private static (RecipeService Service, FakeRecipeRepository Recipes, FakeProductRepository Products, FakeTestDefinitionRepository Definitions, FixedClock Clock) Create()
    {
        var clock = new FixedClock();
        var recipes = new FakeRecipeRepository();
        var products = new FakeProductRepository();
        var definitions = new FakeTestDefinitionRepository();
        var audit = new FakeAuditLog();
        var service = new RecipeService(recipes, products, definitions, clock, audit);

        var type = new ProductType { Code = "PT", Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new ProductModel { ProductTypeId = type.Id, Code = "M1", Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();
        var item = new TestItemDefinition { Code = "IT1", Name = "耐压", ExecutorCode = "PressureExecutor", ResultKind = "PassFail", CreatedAtUtc = clock.UtcNow };
        definitions.Items.Add(item);
        item.Id = 1;
        definitions.Parameters.Add(new ParameterDefinition
        {
            Id = 1,
            TestItemDefinitionId = item.Id,
            Code = "Voltage",
            Name = "试验电压",
            DataType = ParameterDataType.Decimal,
            Unit = "kV",
            IsRequired = true,
            MinValue = 0,
            MaxValue = 50,
            SortOrder = 1
        });
        return (service, recipes, products, definitions, clock);
    }

    [Fact]
    public async Task CreateDraft_AddItem_SetValue_Publish_Success()
    {
        var (service, recipes, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageRecipes);

        var draft = await service.CreateDraftAsync(actor, 1, "配方一");
        await service.AddItemAsync(actor, draft.Id, 1, 1);
        await service.SetParameterValueAsync(actor, draft.Id, 1, 1, "10");
        var errors = await service.ValidateDraftAsync(draft.Id);

        Assert.Empty(errors);
        await service.PublishAsync(actor, draft.Id);
        Assert.Equal(RecipeStatus.Published, draft.Status);
    }

    [Fact]
    public async Task Publish_MissingRequiredParameter_Fails()
    {
        var (service, recipes, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageRecipes);

        var draft = await service.CreateDraftAsync(actor, 1, "配方二");
        await service.AddItemAsync(actor, draft.Id, 1, 1);

        var errors = await service.ValidateDraftAsync(draft.Id);
        Assert.NotEmpty(errors);

        await Assert.ThrowsAsync<DomainException>(() => service.PublishAsync(actor, draft.Id));
        Assert.Equal(RecipeStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task PublishedRecipe_CannotBeEditedDirectly_AndCanCopyNewVersion()
    {
        var (service, recipes, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageRecipes);

        var draft = await service.CreateDraftAsync(actor, 1, "配方三");
        await service.AddItemAsync(actor, draft.Id, 1, 1);
        await service.SetParameterValueAsync(actor, draft.Id, 1, 1, "20");
        await service.PublishAsync(actor, draft.Id);

        await Assert.ThrowsAsync<DomainException>(() => service.AddItemAsync(actor, draft.Id, 1, 2));

        var copy = await service.NewVersionFromAsync(actor, draft.Id);
        Assert.Equal(RecipeStatus.Draft, copy.Status);
        Assert.Equal(2, copy.Version);
        Assert.Single(await recipes.ListItemsAsync(copy.Id));
    }

    [Fact]
    public async Task CreateDraft_RequiresManageRecipes()
    {
        var (service, _, _, _, _) = Create();
        var operatorActor = TestContexts.With(PermissionCode.ExecuteTests);
        await Assert.ThrowsAsync<AuthorizationException>(() => service.CreateDraftAsync(operatorActor, 1, "x"));
    }
}
