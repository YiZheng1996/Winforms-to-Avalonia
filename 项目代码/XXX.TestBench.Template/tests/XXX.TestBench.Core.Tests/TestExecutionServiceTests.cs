using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Domain.TestParameters;
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
            var effective = context.EffectiveParameters;
            return Task.FromResult(Fail
                ? new ItemExecutionOutcome(ItemResultState.Failed, "0.0", "执行失败")
                : new ItemExecutionOutcome(ItemResultState.Passed, $"{effective?.TestVoltageV:0.0}V", "仿真执行通过"));
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
        public required FakeTestParameterRepository Parameters { get; init; }
        public int ProductModelId { get; init; }
        public int ItemDefinitionId { get; init; }
    }

    private static Context Create()
    {
        var clock = new FixedClock();
        var tasks = new FakeTaskRepository();
        var products = new FakeProductRepository();
        var definitions = new FakeTestDefinitionRepository();
        var parameters = new FakeTestParameterRepository();
        var audit = new FakeAuditLog();
        var executors = new FixedExecutorFactory();

        var type = new Domain.Products.ProductType { Id = 1, Code = "PT", Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new Domain.Products.ProductModel { Id = 1, ProductTypeId = type.Id, Code = "M1", Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();

        var item = new TestItemDefinition { Id = 1, Code = "PRESSURE", Name = "耐压试验", ExecutorCode = "PressureExecutor", ResultKind = "PassFail", SortOrder = 1, CreatedAtUtc = clock.UtcNow };
        definitions.Items.Add(item);

        parameters.Project = new ProjectTestParameter { TestTimeSeconds = 60, UpdatedBy = "tester", UpdatedAtUtc = clock.UtcNow };
        parameters.TypeParameters.Add(new ProductTypeTestParameter { ProductTypeId = type.Id, TestVoltageV = 5000, UpdatedBy = "tester", UpdatedAtUtc = clock.UtcNow });
        parameters.ModelParameters.Add(new ProductModelTestParameter { ProductModelId = model.Id, ProtectCurrentMa = 100, UpdatedBy = "tester", UpdatedAtUtc = clock.UtcNow });

        var taskService = new TaskService(tasks, products, clock, audit);
        var testParameters = new TestParameterService(parameters, products, clock, audit);
        var service = new TestExecutionService(tasks, definitions, executors, taskService, testParameters, clock, audit);
        return new Context
        {
            Execution = service,
            Tasks = taskService,
            TaskRepo = tasks,
            Executors = executors,
            Parameters = parameters,
            ProductModelId = model.Id,
            ItemDefinitionId = item.Id
        };
    }

    private static async Task<TestTask> CreateReadyTaskAsync(Context ctx, UserContext actor)
    {
        var task = await ctx.Tasks.CreateAsync(actor, ctx.ProductModelId, new ProductIdentity("SN001", null, null, null));
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
    public async Task Start_WithoutCompleteParameters_IsRejected_TaskStaysReady()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);
        var task = await CreateReadyTaskAsync(ctx, actor);
        ctx.Parameters.ModelParameters.Clear();

        var runtime = new FakeRuntime(isSimulation: true);
        await Assert.ThrowsAsync<DomainException>(() => ctx.Execution.StartAsync(actor, task.Id, DeviceMode.Simulation, runtime));
        Assert.Empty(ctx.TaskRepo.Records);
        Assert.Equal(TaskState.Ready, task.State);
    }

    [Fact]
    public async Task ExecuteAndComplete_PassFlow()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);
        var task = await CreateReadyTaskAsync(ctx, actor);
        var runtime = new FakeRuntime(isSimulation: true);

        var record = await ctx.Execution.StartAsync(actor, task.Id, DeviceMode.Simulation, runtime);
        Assert.NotNull(record.ParameterSnapshot);
        var result = await ctx.Execution.ExecuteItemAsync(actor, record.Id, ctx.ItemDefinitionId, runtime);
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
        await ctx.Execution.ExecuteItemAsync(actor, record.Id, ctx.ItemDefinitionId, runtime);
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
        await Assert.ThrowsAsync<AuthorizationException>(() => ctx.Execution.ExecuteItemAsync(viewer, record.Id, ctx.ItemDefinitionId, runtime));
    }
}
