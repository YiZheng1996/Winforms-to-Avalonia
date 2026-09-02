using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Time;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

/// <summary>
/// 事务原子性：UoW 回滚不留残留；带事务工厂的任务启动正向原子提交。
/// </summary>
public class UnitOfWorkRollbackTests
{
    [Fact]
    public async Task Rollback_RemovesInsertedRows()
    {
        using var env = TestEnv.Create();
        var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, new Pbkdf2PasswordHasher()).InitializeAsync();

        var productRepo = new ProductRepository(factory);
        var type = new XXX.TestBench.Core.Domain.Products.ProductType { Code = "PT-ROLLBACK", Name = "回滚测试", CreatedAtUtc = DateTime.UtcNow };

        await using (var uow = new SqliteUnitOfWork(factory))
        {
            await uow.BeginTransactionAsync();
            await productRepo.AddTypeAsync(type);
            await uow.RollbackAsync();
        }

        Assert.Null(await productRepo.GetTypeByCodeAsync("PT-ROLLBACK")); // 回滚后不存在
    }

    [Fact]
    public async Task TaskStart_WithUnitOfWorkFactory_CommitsAtomically()
    {
        using var env = TestEnv.Create();
        var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, new Pbkdf2PasswordHasher()).InitializeAsync();

        var clock = new SystemClock();
        var audit = new SqliteAuditLog(factory);
        var userRepo = new UserRepository(factory);
        var productRepo = new ProductRepository(factory);
        var taskRepo = new TaskRepository(factory);
        var uowFactory = new SqliteUnitOfWorkFactory(factory);

        var admin = await userRepo.GetByLoginNameAsync("admin") ?? throw new InvalidOperationException("seed missing");
        var role = await userRepo.GetRoleAsync(admin.RoleId) ?? throw new InvalidOperationException("role missing");
        var actor = new UserContext { UserId = admin.Id, LoginName = admin.LoginName, DisplayName = admin.DisplayName, Role = role };

        var products = new ProductService(productRepo, clock, audit);
        var type = await products.CreateTypeAsync(actor, "PT", "压力试验");
        var model = await products.CreateModelAsync(actor, type.Id, "M1", "型号1");

        var tasks = new TaskService(taskRepo, productRepo, clock, audit, uowFactory);
        var task = await tasks.CreateAsync(actor, model.Id, new ProductIdentity("SN001", null, null, null));
        await tasks.ToReadyAsync(actor, task.Id);

        var record = await tasks.StartAsync(actor, task.Id, XXX.TestBench.Core.Domain.Devices.DeviceMode.Simulation, "{\"snapshot\":1}");

        var reloaded = await taskRepo.GetAsync(task.Id);
        Assert.Equal(TaskState.Running, reloaded!.State);
        var persistedRecord = await taskRepo.GetRecordAsync(record.Id);
        Assert.NotNull(persistedRecord);
        Assert.Equal("{\"snapshot\":1}", persistedRecord!.ParameterSnapshot);
    }
}
