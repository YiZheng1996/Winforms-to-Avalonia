using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Time;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

/// <summary>阶段 2 全链路：产品→项点→配方草稿→发布→任务→启动(Simulation)→完成→重启回读。</summary>
public class FullLoopTests
{
    [Fact]
    public async Task CompleteBusinessLoop_PersistsAndSurvivesRestart()
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
        var taskRepo = new TaskRepository(factory);

        var admin = await userRepo.GetByLoginNameAsync("admin") ?? throw new InvalidOperationException("admin seed missing");
        var role = await userRepo.GetRoleAsync(admin.RoleId) ?? throw new InvalidOperationException("role missing");
        var actor = new UserContext { UserId = admin.Id, LoginName = admin.LoginName, DisplayName = admin.DisplayName, Role = role };

        // 主数据：产品类型/型号
        var type = new ProductType { Code = "PT", Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        await productRepo.AddTypeAsync(type);
        var model = new ProductModel { ProductTypeId = type.Id, Code = "M1", Name = "型号1", CreatedAtUtc = clock.UtcNow };
        await productRepo.AddModelAsync(model);

        // 项点与参数
        var item = new TestItemDefinition { Code = "IT1", Name = "耐压", ExecutorCode = "PressureExecutor", ResultKind = "PassFail", CreatedAtUtc = clock.UtcNow };
        await defRepo.AddItemAsync(item);
        var param = new ParameterDefinition
        {
            TestItemDefinitionId = item.Id,
            Code = "Voltage",
            Name = "试验电压",
            DataType = ParameterDataType.Decimal,
            Unit = "kV",
            IsRequired = true,
            MinValue = 0,
            MaxValue = 50,
            SortOrder = 1
        };
        await defRepo.AddParameterAsync(param);

        // 配方：草稿→填参数→发布（事务）
        var recipes = new RecipeService(recipeRepo, productRepo, defRepo, clock, audit);
        var draft = await recipes.CreateDraftAsync(actor, model.Id, "配方1");
        await recipes.AddItemAsync(actor, draft.Id, item.Id, 1);
        var recipeItems = await recipeRepo.ListItemsAsync(draft.Id);
        await recipes.SetParameterValueAsync(actor, draft.Id, recipeItems[0].Id, param.Id, "10");

        await using (var uow = new SqliteUnitOfWork(factory))
        {
            await uow.BeginTransactionAsync();
            await recipes.PublishAsync(actor, draft.Id);
            await uow.CommitAsync();
        }
        var publishedRecipe = await recipeRepo.GetAsync(draft.Id);
        Assert.Equal(XXX.TestBench.Core.Domain.Recipes.RecipeStatus.Published, publishedRecipe!.Status);

        // 任务：创建→就绪→启动→完成
        var tasks = new TaskService(taskRepo, productRepo, recipeRepo, clock, audit);
        var task = await tasks.CreateAsync(actor, model.Id, draft.Id, new ProductIdentity("SN001", "B2026", "S1", "测试"));
        await tasks.ToReadyAsync(actor, task.Id);

        TestRecord record;
        await using (var uow = new SqliteUnitOfWork(factory))
        {
            await uow.BeginTransactionAsync();
            record = await tasks.StartAsync(actor, task.Id, DeviceMode.Simulation);
            await uow.CommitAsync();
        }
        await tasks.CompleteAsync(actor, task.Id, "通过");

        // 重启回读
        var factory2 = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory2, hasher).InitializeAsync();
        var taskRepo2 = new TaskRepository(factory2);
        var reloaded = await taskRepo2.GetAsync(task.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(TaskState.Completed, reloaded!.State);
        Assert.Equal("SN001", reloaded.ProductIdentity.ProductNumber);

        await using var conn = factory2.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM test_records WHERE task_id=$id";
        cmd.Parameters.AddWithValue("$id", task.Id);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }
}
