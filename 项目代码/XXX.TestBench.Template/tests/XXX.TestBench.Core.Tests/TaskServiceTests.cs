using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Domain.Tasks;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class TaskServiceTests
{
    private static (TaskService Service, FakeTaskRepository Tasks, FakeRecipeRepository Recipes, FakeProductRepository Products, FixedClock Clock) Create()
    {
        var clock = new FixedClock();
        var tasks = new FakeTaskRepository();
        var products = new FakeProductRepository();
        var recipes = new FakeRecipeRepository();
        var audit = new FakeAuditLog();
        var service = new TaskService(tasks, products, recipes, clock, audit);

        var type = new Domain.Products.ProductType { Code = "PT", Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new Domain.Products.ProductModel { ProductTypeId = type.Id, Code = "M1", Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();

        var published = new RecipeVersion
        {
            Id = 10,
            ProductModelId = model.Id,
            Version = 1,
            Name = "已发布配方",
            CreatedByUserId = 1,
            CreatedAtUtc = clock.UtcNow
        };
        published.Publish(1, clock.UtcNow);
        recipes.AddAsync(published).Wait();
        return (service, tasks, recipes, products, clock);
    }

    [Fact]
    public async Task CreateTask_OnlyWithPublishedMatchingRecipe()
    {
        var (service, tasks, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks);

        var task = await service.CreateAsync(actor, 1, 10, new ProductIdentity("SN001", null, null, null));

        Assert.Equal(TaskState.Draft, task.State);
        Assert.StartsWith("T-", task.TaskNumber);
        Assert.Single(tasks.Tasks);
    }

    [Fact]
    public async Task CreateTask_RejectsDraftRecipe()
    {
        var (service, _, recipes, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks);
        var draft = new RecipeVersion { Id = 11, ProductModelId = 1, Version = 2, Name = "草稿", CreatedByUserId = 1, CreatedAtUtc = DateTime.UtcNow };
        await recipes.AddAsync(draft);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateAsync(actor, 1, draft.Id, new ProductIdentity("SN", null, null, null)));
    }

    [Fact]
    public async Task Start_CreatesRecordSnapshot_AndMovesTaskToRunning()
    {
        var (service, tasks, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);

        var task = await service.CreateAsync(actor, 1, 10, new ProductIdentity("SN001", null, null, null));
        await service.ToReadyAsync(actor, task.Id);
        var record = await service.StartAsync(actor, task.Id, DeviceMode.Simulation);

        Assert.Equal(TaskState.Running, task.State);
        Assert.Equal(DeviceMode.Simulation, record.DeviceMode);
        Assert.Equal(1, record.RecipeVersionNumber);
        Assert.Single(tasks.Records);
    }

    [Fact]
    public async Task Cancel_CompletedTask_IsRejected()
    {
        var (service, tasks, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);

        var task = await service.CreateAsync(actor, 1, 10, new ProductIdentity("SN001", null, null, null));
        await service.ToReadyAsync(actor, task.Id);
        await service.StartAsync(actor, task.Id, DeviceMode.Simulation);
        await service.CompleteAsync(actor, task.Id, "通过");

        await Assert.ThrowsAsync<DomainException>(() => service.CancelAsync(actor, task.Id));
        Assert.Equal(TaskState.Completed, task.State);
    }

    [Fact]
    public async Task CreateTask_RequiresPermission()
    {
        var (service, _, _, _, _) = Create();
        var viewer = TestContexts.With(PermissionCode.ViewRecords);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            service.CreateAsync(viewer, 1, 10, new ProductIdentity("SN", null, null, null)));
    }
}
