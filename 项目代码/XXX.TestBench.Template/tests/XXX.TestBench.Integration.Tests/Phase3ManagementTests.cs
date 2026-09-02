using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Time;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

/// <summary>
/// 阶段 3 集成：产品/项点/参数管理 + 配方编辑（upsert/移除/重命名/模板）→发布→复制→停用→重启回读。
/// </summary>
public class Phase3ManagementTests
{
    [Fact]
    public async Task ProductDefinitionRecipeManagementLoop_PersistsAcrossRestart()
    {
        using var env = TestEnv.Create();
        var hasher = new Pbkdf2PasswordHasher();
        var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, hasher).InitializeAsync();

        var clock = new SystemClock();
        var audit = new SqliteAuditLog(factory);
        var userRepo = new UserRepository(factory);
        var productRepo = new ProductRepository(factory);
        var defRepo = new TestDefinitionRepository(factory);
        var recipeRepo = new RecipeRepository(factory);
        var admin = await userRepo.GetByLoginNameAsync("admin") ?? throw new InvalidOperationException("seed missing");
        var role = await userRepo.GetRoleAsync(admin.RoleId) ?? throw new InvalidOperationException("role missing");
        var actor = new UserContext { UserId = admin.Id, LoginName = admin.LoginName, DisplayName = admin.DisplayName, Role = role };

        // 产品
        var products = new ProductService(productRepo, clock, audit);
        var type = await products.CreateTypeAsync(actor, "PT", "压力试验");
        var model = await products.CreateModelAsync(actor, type.Id, "M1", "型号1");

        // 项点与参数
        var definitions = new TestDefinitionService(defRepo, clock, audit);
        var item1 = await definitions.CreateItemAsync(actor, "IT1", "耐压", "PressureExecutor", "PassFail", 1);
        var item2 = await definitions.CreateItemAsync(actor, "IT2", "绝缘", "InsulationExecutor", "PassFail", 2);
        var voltage = await definitions.CreateParameterAsync(actor, item1.Id, "Voltage", "试验电压", ParameterDataType.Decimal, true, "kV", 0, 50, 2, null, 1);
        var insulation = await definitions.CreateParameterAsync(actor, item2.Id, "Resistance", "绝缘电阻", ParameterDataType.Decimal, true, "MΩ", 0, 1000, 0, null, 1);

        // 配方：两件项点、参数 upsert、重命名、模板、移除再添加
        var recipes = new RecipeService(recipeRepo, productRepo, defRepo, clock, audit);
        var draft = await recipes.CreateDraftAsync(actor, model.Id, "配方一");
        await recipes.AddItemAsync(actor, draft.Id, item1.Id, 1);
        await recipes.AddItemAsync(actor, draft.Id, item2.Id, 2);
        var items = await recipeRepo.ListItemsAsync(draft.Id);
        await recipes.SetParameterValueAsync(actor, draft.Id, items[0].Id, voltage.Id, "10");
        await recipes.SetParameterValueAsync(actor, draft.Id, items[0].Id, voltage.Id, "20"); // upsert
        await recipes.SetParameterValueAsync(actor, draft.Id, items[1].Id, insulation.Id, "500");
        await recipes.RenameDraftAsync(actor, draft.Id, "配方一-修订");
        await recipes.SetReportTemplateAsync(actor, draft.Id, "assets/report-templates/standard.xlsx");

        var values = await recipeRepo.ListParameterValuesAsync(draft.Id);
        Assert.Equal(2, values.Count);
        Assert.Contains(values, v => v.RawValue == "20");

        // 发布
        await recipes.PublishAsync(actor, draft.Id);
        var published = await recipeRepo.GetAsync(draft.Id);
        Assert.Equal(RecipeStatus.Published, published!.Status);
        Assert.Equal("assets/report-templates/standard.xlsx", published.ReportTemplatePath);

        // 复制新版本并停用旧版
        var copy = await recipes.NewVersionFromAsync(actor, draft.Id);
        Assert.Equal(RecipeStatus.Draft, copy.Status);
        Assert.Equal(2, copy.Version);
        Assert.Equal(2, (await recipeRepo.ListItemsAsync(copy.Id)).Count);
        Assert.Equal(2, (await recipeRepo.ListParameterValuesAsync(copy.Id)).Count);
        await recipes.RetireAsync(actor, draft.Id);
        Assert.Equal(RecipeStatus.Retired, (await recipeRepo.GetAsync(draft.Id))!.Status);

        // 重启回读
        var factory2 = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory2, hasher).InitializeAsync();
        var productRepo2 = new ProductRepository(factory2);
        var defRepo2 = new TestDefinitionRepository(factory2);
        var recipeRepo2 = new RecipeRepository(factory2);

        Assert.Single(await productRepo2.ListTypesAsync(includeDisabled: true));
        Assert.Single(await productRepo2.ListModelsAsync(type.Id, includeDisabled: true));
        Assert.Equal(3, (await defRepo2.ListItemsAsync(includeDisabled: true)).Count); // 2 个测试项点 + 1 个内建固定项点
        Assert.Equal(2, (await recipeRepo2.ListByModelAsync(model.Id, includeRetired: true)).Count);
        var reloaded = await recipeRepo2.GetAsync(draft.Id);
        Assert.Equal(RecipeStatus.Retired, reloaded!.Status);
    }
}
