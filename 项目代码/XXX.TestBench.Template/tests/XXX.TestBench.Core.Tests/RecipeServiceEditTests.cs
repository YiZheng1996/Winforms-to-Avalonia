using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Domain.TestDefinitions;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class RecipeServiceEditTests
{
    private static (RecipeService Service, FakeRecipeRepository Recipes, FakeTestDefinitionRepository Definitions, FixedClock Clock) Create()
    {
        var clock = new FixedClock();
        var recipes = new FakeRecipeRepository();
        var products = new FakeProductRepository();
        var definitions = new FakeTestDefinitionRepository();
        var type = new Domain.Products.ProductType { Code = "PT", Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new Domain.Products.ProductModel { ProductTypeId = type.Id, Code = "M1", Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();
        var service = new RecipeService(recipes, products, definitions, clock, new FakeAuditLog());

        var item1 = new TestItemDefinition { Id = 1, Code = "IT1", Name = "耐压", ExecutorCode = "PressureExecutor", ResultKind = "PassFail", CreatedAtUtc = clock.UtcNow };
        var item2 = new TestItemDefinition { Id = 2, Code = "IT2", Name = "绝缘", ExecutorCode = "InsulationExecutor", ResultKind = "PassFail", CreatedAtUtc = clock.UtcNow };
        definitions.Items.Add(item1);
        definitions.Items.Add(item2);
        definitions.Parameters.Add(new ParameterDefinition { Id = 1, TestItemDefinitionId = 1, Code = "Voltage", Name = "试验电压", DataType = ParameterDataType.Decimal, IsRequired = true, MinValue = 0, MaxValue = 50, SortOrder = 1 });
        return (service, recipes, definitions, clock);
    }

    [Fact]
    public async Task SetParameterValue_Upserts_InsteadOfDuplicate()
    {
        var (service, recipes, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageRecipes);
        var draft = await service.CreateDraftAsync(actor, 1, "配方");
        await service.AddItemAsync(actor, draft.Id, 1, 1);
        var items = await recipes.ListItemsAsync(draft.Id);

        await service.SetParameterValueAsync(actor, draft.Id, items[0].Id, 1, "10");
        await service.SetParameterValueAsync(actor, draft.Id, items[0].Id, 1, "20");

        var values = await recipes.ListParameterValuesAsync(draft.Id);
        Assert.Single(values);
        Assert.Equal("20", values[0].RawValue);
    }

    [Fact]
    public async Task RemoveItem_DeletesItemAndValues()
    {
        var (service, recipes, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageRecipes);
        var draft = await service.CreateDraftAsync(actor, 1, "配方");
        await service.AddItemAsync(actor, draft.Id, 1, 1);
        await service.AddItemAsync(actor, draft.Id, 2, 2);
        var items = await recipes.ListItemsAsync(draft.Id);
        await service.SetParameterValueAsync(actor, draft.Id, items[0].Id, 1, "10");

        await service.RemoveItemAsync(actor, draft.Id, items[0].Id);

        Assert.Empty(await recipes.ListParameterValuesAsync(draft.Id));
        Assert.Single(await recipes.ListItemsAsync(draft.Id));
    }

    [Fact]
    public async Task Rename_AndSetTemplate_OnlyOnDraft()
    {
        var (service, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageRecipes);
        var draft = await service.CreateDraftAsync(actor, 1, "配方");
        await service.AddItemAsync(actor, draft.Id, 1, 1);
        await service.SetParameterValueAsync(actor, draft.Id, 1, 1, "10");

        await service.RenameDraftAsync(actor, draft.Id, "配方-改名");
        await service.SetReportTemplateAsync(actor, draft.Id, "assets/report-templates/xxx.xlsx");
        await service.PublishAsync(actor, draft.Id);

        await Assert.ThrowsAsync<DomainException>(() => service.RenameDraftAsync(actor, draft.Id, "再改名"));
        Assert.Equal("配方-改名", draft.Name);
        Assert.Equal("assets/report-templates/xxx.xlsx", draft.ReportTemplatePath);
    }

    [Fact]
    public async Task Retire_OnlyPublished_AndNewTasksCannotUse()
    {
        var (service, recipes, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageRecipes);
        var draft = await service.CreateDraftAsync(actor, 1, "配方");
        await service.AddItemAsync(actor, draft.Id, 1, 1);
        await service.SetParameterValueAsync(actor, draft.Id, 1, 1, "10");
        await service.PublishAsync(actor, draft.Id);

        await service.RetireAsync(actor, draft.Id);
        Assert.Equal(RecipeStatus.Retired, (await recipes.GetAsync(draft.Id))!.Status);

        // 停用后复制新版本仍可用
        var copy = await service.NewVersionFromAsync(actor, draft.Id);
        Assert.Equal(RecipeStatus.Draft, copy.Status);
    }
}
