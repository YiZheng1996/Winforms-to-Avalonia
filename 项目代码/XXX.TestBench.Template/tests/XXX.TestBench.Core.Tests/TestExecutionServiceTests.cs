using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class TestExecutionServiceTests
{
    private sealed class FixedExecutor : ITestItemExecutor
    {
        public string ExecutorCode => "PressureExecutor";
        public bool Fail { get; set; }
        public Task<ItemExecutionOutcome> ExecuteAsync(ItemExecutionContext context, CancellationToken ct = default)
        {
            var target = context.ParameterValues.GetValueOrDefault("Voltage");
            return Task.FromResult(Fail
                ? new ItemExecutionOutcome(ItemResultState.Failed, "0.0", $"目标 {target}，实测 0.0")
                : new ItemExecutionOutcome(ItemResultState.Passed, "10.0", $"目标 {target}，实测 10.0"));
        }
    }

    private sealed class FixedExecutorFactory : ITestItemExecutorFactory
    {
        public FixedExecutor Executor { get; } = new();
        public ITestItemExecutor Get(string executorCode) => Executor;
    }

    private sealed class Context
    {
        public required TestExecutionService Execution { get; init; }
        public required TaskService Tasks { get; init; }
        public required FakeTaskRepository TaskRepo { get; init; }
        public required FixedExecutorFactory Executors { get; init; }
        public int ProductModelId { get; init; }
    }

    private static Context Create()
    {
        var clock = new FixedClock();
        var tasks = new FakeTaskRepository();
        var products = new FakeProductRepository();
        var recipes = new FakeRecipeRepository();
        var definitions = new FakeTestDefinitionRepository();
        var audit = new FakeAuditLog();
        var executors = new FixedExecutorFactory();

        var type = new Domain.Products.ProductType { Code = "PT", Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new Domain.Products.ProductModel { ProductTypeId = type.Id, Code = "M1", Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();

        var item = new TestItemDefinition { Id = 1, Code = "IT1", Name = "耐压", ExecutorCode = "PressureExecutor", ResultKind = "PassFail", CreatedAtUtc = clock.UtcNow };
        definitions.Items.Add(item);
        definitions.Parameters.Add(new ParameterDefinition { Id = 1, TestItemDefinitionId = 1, Code = "Voltage", Name = "试验电压", DataType = ParameterDataType.Decimal, IsRequired = true, MinValue = 0, MaxValue = 50, SortOrder = 1 });

        var recipe = new RecipeVersion { Id = 10, ProductModelId = model.Id, Version = 1, Name = "已发布", CreatedByUserId = 1, CreatedAtUtc = clock.UtcNow };
        recipe.Publish(1, clock.UtcNow);
        recipes.AddAsync(recipe).Wait();
        recipes.AddItemAsync(new RecipeItem { Id = 100, RecipeVersionId = 10, TestItemDefinitionId = 1, SortOrder = 1 }).Wait();
        recipes.AddParameterValueAsync(new RecipeParameterValue { Id = 200, RecipeItemId = 100, ParameterDefinitionId = 1, RawValue = "10" }).Wait();

        var taskService = new TaskService(tasks, products, recipes, clock, audit);
        var service = new TestExecutionService(tasks, recipes, definitions, executors, taskService, clock, audit);
        return new Context { Execution = service, Tasks = taskService, TaskRepo = tasks, Executors = executors, ProductModelId = model.Id };
    }

    private static async Task<TestTask> CreateReadyTaskAsync(Context ctx, UserContext actor)
    {
        var task = await ctx.Tasks.CreateAsync(actor, ctx.ProductModelId, 10, new ProductIdentity("SN001", null, null, null));
        await ctx.Tasks.ToReadyAsync(actor, task.Id);
        return task;
    }

    [Fact]
    public async Task Precheck_RejectsFaultedRuntime()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);
        var task = await CreateReadyTaskAsync(ctx, actor);

        var faulted = new FakeRuntime(isSimulation: true, health: DeviceHealth.Faulted, connected: false);
        await Assert.ThrowsAsync<DomainException>(() => ctx.Execution.StartAsync(actor, task.Id, DeviceMode.Simulation, faulted));
        Assert.Empty(ctx.TaskRepo.Records);
    }

    [Fact]
    public async Task ExecuteAndComplete_PassFlow()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);
        var task = await CreateReadyTaskAsync(ctx, actor);
        var runtime = new FakeRuntime(isSimulation: true);

        var record = await ctx.Execution.StartAsync(actor, task.Id, DeviceMode.Simulation, runtime);
        var result = await ctx.Execution.ExecuteItemAsync(actor, record.Id, 100, runtime);
        var conclusion = await ctx.Execution.CompleteAsync(actor, record.Id);

        Assert.Equal(ItemResultState.Passed, result.State);
        Assert.Equal("全部通过（1/1）", conclusion);
        Assert.Equal(TaskState.Completed, task.State);
        Assert.Equal(RecordState.Completed, (await ctx.TaskRepo.GetRecordAsync(record.Id))!.State);
    }

    [Fact]
    public async Task Complete_WithFailedItem_MarksRecordAndTaskFailed()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);
        var task = await CreateReadyTaskAsync(ctx, actor);
        var runtime = new FakeRuntime(isSimulation: true);
        ctx.Executors.Executor.Fail = true;

        var record = await ctx.Execution.StartAsync(actor, task.Id, DeviceMode.Simulation, runtime);
        await ctx.Execution.ExecuteItemAsync(actor, record.Id, 100, runtime);
        var conclusion = await ctx.Execution.CompleteAsync(actor, record.Id);

        Assert.Contains("失败", conclusion);
        Assert.Equal(TaskState.Failed, task.State);
    }

    [Fact]
    public async Task Complete_WithPendingItem_IsRejected()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);
        var task = await CreateReadyTaskAsync(ctx, actor);
        var runtime = new FakeRuntime(isSimulation: true);

        var record = await ctx.Execution.StartAsync(actor, task.Id, DeviceMode.Simulation, runtime);
        await Assert.ThrowsAsync<DomainException>(() => ctx.Execution.CompleteAsync(actor, record.Id));
    }

    [Fact]
    public async Task Execute_RequiresExecuteTestsPermission()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);
        var task = await CreateReadyTaskAsync(ctx, actor);
        var runtime = new FakeRuntime(isSimulation: true);
        var record = await ctx.Execution.StartAsync(actor, task.Id, DeviceMode.Simulation, runtime);

        var viewer = TestContexts.With(PermissionCode.ViewRecords);
        await Assert.ThrowsAsync<AuthorizationException>(() => ctx.Execution.ExecuteItemAsync(viewer, record.Id, 100, runtime));
    }
}
