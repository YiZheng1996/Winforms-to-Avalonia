using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class TaskServiceTests
{
    private static (TaskService Service, FakeTaskRepository Tasks, FakeProductRepository Products, FixedClock Clock) Create()
    {
        var clock = new FixedClock();
        var tasks = new FakeTaskRepository();
        var products = new FakeProductRepository();
        var audit = new FakeAuditLog();
        var service = new TaskService(tasks, products, clock, audit);

        var type = new Domain.Products.ProductType { Code = "PT", Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new Domain.Products.ProductModel { ProductTypeId = type.Id, Code = "M1", Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();
        return (service, tasks, products, clock);
    }

    [Fact]
    public async Task CreateTask_WithValidModel()
    {
        var (service, tasks, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks);

        var task = await service.CreateAsync(actor, 1, new ProductIdentity("SN001", null, null, null));

        Assert.Equal(TaskState.Draft, task.State);
        Assert.StartsWith("T-", task.TaskNumber);
        Assert.Single(tasks.Tasks);
    }

    [Fact]
    public async Task CreateTask_RequiresProductIdentity()
    {
        var (service, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateAsync(actor, 1, new ProductIdentity(null, null, null, null)));
    }

    [Fact]
    public async Task Start_CreatesRecordSnapshot_AndMovesTaskToRunning()
    {
        var (service, tasks, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);

        var task = await service.CreateAsync(actor, 1, new ProductIdentity("SN001", null, null, null));
        await service.ToReadyAsync(actor, task.Id);
        var record = await service.StartAsync(actor, task.Id, DeviceMode.Simulation, "{\"snapshot\":1}");

        Assert.Equal(TaskState.Running, task.State);
        Assert.Equal(DeviceMode.Simulation, record.DeviceMode);
        Assert.Equal("{\"snapshot\":1}", record.ParameterSnapshot);
        Assert.Single(tasks.Records);
    }

    [Fact]
    public async Task Cancel_CompletedTask_IsRejected()
    {
        var (service, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);

        var task = await service.CreateAsync(actor, 1, new ProductIdentity("SN001", null, null, null));
        await service.ToReadyAsync(actor, task.Id);
        await service.StartAsync(actor, task.Id, DeviceMode.Simulation, "{}");
        await service.CompleteAsync(actor, task.Id, "通过");

        await Assert.ThrowsAsync<DomainException>(() => service.CancelAsync(actor, task.Id));
        Assert.Equal(TaskState.Completed, task.State);
    }

    [Fact]
    public async Task CreateTask_RequiresPermission()
    {
        var (service, _, _, _) = Create();
        var viewer = TestContexts.With(PermissionCode.ViewRecords);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            service.CreateAsync(viewer, 1, new ProductIdentity("SN", null, null, null)));
    }

    [Fact]
    public async Task UpdateIdentity_OnlyOnDraft_AndRequiresValue()
    {
        var (service, _, _, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTasks, PermissionCode.ExecuteTests);
        var task = await service.CreateAsync(actor, 1, new ProductIdentity("SN001", null, null, null));

        await service.UpdateIdentityAsync(actor, task.Id, new ProductIdentity("SN002", "B2", null, null));
        Assert.Equal("SN002", task.ProductIdentity.ProductNumber);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.UpdateIdentityAsync(actor, task.Id, new ProductIdentity(null, null, null, null)));
    }
}
