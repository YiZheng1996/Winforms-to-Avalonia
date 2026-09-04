using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Execution;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class TestPointServiceTests
{
    private sealed class DummyExecutor : ITestItemExecutor
    {
        public string ExecutorCode => "PressureExecutor";
        public Task<ItemExecutionOutcome> ExecuteAsync(ItemExecutionContext context, CancellationToken ct = default)
            => Task.FromResult(new ItemExecutionOutcome(ItemResultState.Passed, null, null));
    }

    private sealed class FixedExecutorFactory : ITestItemExecutorFactory
    {
        private readonly DummyExecutor _executor = new();
        public ITestItemExecutor Get(string executorCode) => _executor;
        public IReadOnlyCollection<string> Codes { get; } = new[] { "PressureExecutor" };
    }

    private sealed class Context
    {
        public required TestPointService Service { get; init; }
        public required FakeProductRepository Products { get; init; }
        public required FakeTestPointRepository Points { get; init; }
        public required FakeModelPointConfigRepository Configs { get; init; }
        public required ProductType Type { get; init; }
        public required ProductModel Model { get; init; }
    }

    private static Context Create()
    {
        var clock = new FixedClock();
        var products = new FakeProductRepository();
        var points = new FakeTestPointRepository();
        var configs = new FakeModelPointConfigRepository();
        var audit = new FakeAuditLog();
        var executors = new FixedExecutorFactory();
        var service = new TestPointService(points, configs, products, executors, clock, audit);

        var type = new ProductType { Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new ProductModel { ProductTypeId = type.Id, Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();
        return new Context { Service = service, Products = products, Points = points, Configs = configs, Type = type, Model = model };
    }

    [Fact]
    public async Task CreatePoint_ValidInput_CreatesPoint()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);

        var point = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);

        Assert.True(point.Id > 0);
        Assert.Equal("耐压试验", point.Name);
        Assert.Equal("PressureExecutor", point.ExecutorCode);
        Assert.Single(ctx.Points.Items);
    }

    [Fact]
    public async Task CreatePoint_UnknownExecutor_IsRejected()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);

        await Assert.ThrowsAsync<DomainException>(() =>
            ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "未知项点", "NotRegistered", "PassFail", 1));
    }

    [Fact]
    public async Task CreatePoint_AssignsIdsWithoutManualCodes()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);
        var first = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);
        var second = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "重复项点", "PressureExecutor", "PassFail", 2);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, ctx.Points.Items.Count);
    }

    [Fact]
    public async Task CreatePoint_RequiresManageTestPointsPermission()
    {
        var ctx = Create();
        var viewer = TestContexts.With(PermissionCode.ViewRecords);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            ctx.Service.CreatePointAsync(viewer, ctx.Type.Id, "耐压试验", "PressureExecutor", "PassFail", 1));
    }

    [Fact]
    public async Task DeletePoint_ReferencedByConfig_IsRejected()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);
        var point = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);
        ctx.Points.ModelReferences.Add(new ModelPointConfig(ctx.Model.Id, point.Id, 1));

        await Assert.ThrowsAsync<DomainException>(() => ctx.Service.DeletePointAsync(actor, point.Id));
    }

    [Fact]
    public async Task DeletePoint_ReferencedByResult_IsRejected()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);
        var point = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);
        ctx.Points.ResultReferences.Add(new TestItemResult { Id = 1, RecordId = 1, TestItemPointId = point.Id });

        await Assert.ThrowsAsync<DomainException>(() => ctx.Service.DeletePointAsync(actor, point.Id));
    }

    [Fact]
    public async Task DeletePoint_Unreferenced_Succeeds()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);
        var point = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);

        await ctx.Service.DeletePointAsync(actor, point.Id);
        Assert.Empty(ctx.Points.Items);
    }

    [Fact]
    public async Task SaveConfiguration_OrdersPoints_AndReturnsSequence()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);
        var p1 = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "项点A", "PressureExecutor", "PassFail", 1);
        var p2 = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "项点B", "PressureExecutor", "PassFail", 2);

        await ctx.Service.SaveConfigurationAsync(actor, ctx.Model.Id, new[] { p2.Id, p1.Id });

        var sequence = await ctx.Service.GetSequenceAsync(ctx.Model.Id);
        Assert.Equal(2, sequence.Count);
        Assert.Equal(p2.Id, sequence[0].Id);
        Assert.Equal(p1.Id, sequence[1].Id);
    }

    [Fact]
    public async Task SaveConfiguration_DisabledPoint_IsRejected()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);
        var point = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);
        await ctx.Service.UpdatePointAsync(actor, point.Id, "耐压试验", "PressureExecutor", "PassFail", isEnabled: false, sortOrder: 1);

        await Assert.ThrowsAsync<DomainException>(() =>
            ctx.Service.SaveConfigurationAsync(actor, ctx.Model.Id, new[] { point.Id }));
    }

    [Fact]
    public async Task GetSequence_OnlyReturnsEnabledConfiguredPoints()
    {
        var ctx = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestPoints);
        var p1 = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "项点A", "PressureExecutor", "PassFail", 1);
        var p2 = await ctx.Service.CreatePointAsync(actor, ctx.Type.Id, "项点B", "PressureExecutor", "PassFail", 2);
        await ctx.Service.SaveConfigurationAsync(actor, ctx.Model.Id, new[] { p1.Id, p2.Id });
        await ctx.Service.UpdatePointAsync(actor, p2.Id, "项点B", "PressureExecutor", "PassFail", isEnabled: false, sortOrder: 2);

        var sequence = await ctx.Service.GetSequenceAsync(ctx.Model.Id);
        Assert.Single(sequence);
        Assert.Equal(p1.Id, sequence[0].Id);
    }
}
