using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Execution;
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
        public IReadOnlyCollection<string> Codes { get; } = new[] { "PressureExecutor" };
    }

    private sealed class Context
    {
        public required TestExecutionService Execution { get; init; }
        public required TestPointService TestPoints { get; init; }
        public required FakeRecordRepository Records { get; init; }
        public required FakeTestPointRepository Points { get; init; }
        public required FakeModelPointConfigRepository Configs { get; init; }
        public required FakeTestParameterRepository Parameters { get; init; }
        public required FixedExecutorFactory Executors { get; init; }
        public required int ProductModelId { get; init; }
        public required int PointId { get; init; }
    }

    private static Context Create(bool withParameters = true)
    {
        var clock = new FixedClock();
        var records = new FakeRecordRepository();
        var products = new FakeProductRepository();
        var points = new FakeTestPointRepository();
        var configs = new FakeModelPointConfigRepository();
        var parameters = new FakeTestParameterRepository();
        var audit = new FakeAuditLog();
        var executors = new FixedExecutorFactory();

        var type = new ProductType { Id = 1, Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new ProductModel { Id = 1, ProductTypeId = type.Id, Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();

        var point = new TestItemPoint
        {
            Id = 1,
            ProductTypeId = type.Id,
            Name = "耐压试验",
            ExecutorCode = "PressureExecutor",
            ResultKind = "PassFail",
            IsEnabled = true,
            SortOrder = 1,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        points.AddAsync(point).Wait();
        configs.ByModel[model.Id] = new List<ModelPointConfig> { new(model.Id, point.Id, 1) };

        if (withParameters)
        {
            parameters.Project = new ProjectTestParameter { TestTimeSeconds = 60, UpdatedBy = "tester", UpdatedAtUtc = clock.UtcNow };
            parameters.TypeParameters.Add(new ProductTypeTestParameter { ProductTypeId = type.Id, TestVoltageV = 5000, UpdatedBy = "tester", UpdatedAtUtc = clock.UtcNow });
            parameters.ModelParameters.Add(new ProductModelTestParameter { ProductModelId = model.Id, ProtectCurrentMa = 100, UpdatedBy = "tester", UpdatedAtUtc = clock.UtcNow });
        }

        var testParameters = new TestParameterService(parameters, products, clock, audit);
        var testPoints = new TestPointService(points, configs, products, executors, clock, audit);
        var service = new TestExecutionService(records, products, executors, testPoints, testParameters, clock, audit);
        return new Context
        {
            Execution = service,
            TestPoints = testPoints,
            Records = records,
            Points = points,
            Configs = configs,
            Parameters = parameters,
            Executors = executors,
            ProductModelId = model.Id,
            PointId = point.Id
        };
    }

    private static ProductIdentity Identity(string? productNumber = "SN001") => new(productNumber, null, null, null);

    [Fact]
    public async Task Precheck_RejectsFaultedRuntime()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ExecuteTests);

        var faulted = new FakeRuntime(isSimulation: true, health: DeviceHealth.Faulted, connected: false);
        await Assert.ThrowsAsync<DomainException>(() =>
            ctx.Execution.StartAsync(actor, ctx.ProductModelId, Identity(), DeviceMode.Simulation, faulted));
        Assert.Empty(ctx.Records.Records);
    }

    [Fact]
    public async Task Start_WithoutCompleteParameters_IsRejected()
    {
        var ctx = Create(withParameters: false);
        var actor = TestContexts.With(PermissionCode.ExecuteTests);
        var runtime = new FakeRuntime(isSimulation: true);

        await Assert.ThrowsAsync<DomainException>(() =>
            ctx.Execution.StartAsync(actor, ctx.ProductModelId, Identity(), DeviceMode.Simulation, runtime));
        Assert.Empty(ctx.Records.Records);
    }

    [Fact]
    public async Task Start_WithoutConfiguredPoints_IsRejected()
    {
        var ctx = Create();
        ctx.Configs.ByModel[ctx.ProductModelId] = new List<ModelPointConfig>();
        var actor = TestContexts.With(PermissionCode.ExecuteTests);
        var runtime = new FakeRuntime(isSimulation: true);

        await Assert.ThrowsAsync<DomainException>(() =>
            ctx.Execution.StartAsync(actor, ctx.ProductModelId, Identity(), DeviceMode.Simulation, runtime));
        Assert.Empty(ctx.Records.Records);
    }

    [Fact]
    public async Task Start_RequiresProductIdentity()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ExecuteTests);
        var runtime = new FakeRuntime(isSimulation: true);

        await Assert.ThrowsAsync<DomainException>(() =>
            ctx.Execution.StartAsync(actor, ctx.ProductModelId, Identity(productNumber: null), DeviceMode.Simulation, runtime));
    }

    [Fact]
    public async Task Start_CreatesRecordWithParameterAndSequenceSnapshots()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ExecuteTests);
        var runtime = new FakeRuntime(isSimulation: true);

        var record = await ctx.Execution.StartAsync(actor, ctx.ProductModelId, Identity(), DeviceMode.Simulation, runtime);

        Assert.StartsWith("R-", record.RecordNumber);
        Assert.Equal(RecordState.Running, record.State);
        Assert.NotNull(record.ParameterSnapshot);
        Assert.NotNull(record.SequenceSnapshot);
        Assert.Single(ctx.Records.Records);

        var sequence = await ctx.Execution.GetSequenceAsync(record.Id);
        Assert.Single(sequence);
        Assert.Equal(ctx.PointId, sequence[0].PointId);
        Assert.Equal("耐压试验", sequence[0].Name);
    }

    [Fact]
    public async Task ExecuteItem_Passes_AndPersistsResult()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ExecuteTests);
        var runtime = new FakeRuntime(isSimulation: true);
        var record = await ctx.Execution.StartAsync(actor, ctx.ProductModelId, Identity(), DeviceMode.Simulation, runtime);

        var result = await ctx.Execution.ExecuteItemAsync(actor, record.Id, ctx.PointId, runtime);

        Assert.Equal(ItemResultState.Passed, result.State);
        Assert.Single(ctx.Records.Results);
        Assert.Equal(ctx.PointId, result.TestItemPointId);
    }

    [Fact]
    public async Task Complete_RequiresAllItemsExecuted()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ExecuteTests);
        var runtime = new FakeRuntime(isSimulation: true);
        var record = await ctx.Execution.StartAsync(actor, ctx.ProductModelId, Identity(), DeviceMode.Simulation, runtime);

        await Assert.ThrowsAsync<DomainException>(() => ctx.Execution.CompleteAsync(actor, record.Id));

        await ctx.Execution.ExecuteItemAsync(actor, record.Id, ctx.PointId, runtime);
        var conclusion = await ctx.Execution.CompleteAsync(actor, record.Id);
        Assert.Contains("全部通过", conclusion);
        Assert.Equal(RecordState.Completed, ctx.Records.Records[0].State);
    }

    [Fact]
    public async Task ExecuteItem_RequiresExecutePermission()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ExecuteTests);
        var runtime = new FakeRuntime(isSimulation: true);
        var record = await ctx.Execution.StartAsync(actor, ctx.ProductModelId, Identity(), DeviceMode.Simulation, runtime);

        var viewer = TestContexts.With(PermissionCode.ViewRecords);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            ctx.Execution.ExecuteItemAsync(viewer, record.Id, ctx.PointId, runtime));
    }
}
