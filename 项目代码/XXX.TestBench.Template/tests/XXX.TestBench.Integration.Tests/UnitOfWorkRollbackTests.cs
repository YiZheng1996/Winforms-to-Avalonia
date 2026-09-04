using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Time;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

/// <summary>
/// 事务原子性：UoW 回滚不留残留；记录完成正向原子提交。
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
        var type = new ProductType { Name = "回滚测试", CreatedAtUtc = DateTime.UtcNow };

        await using (var uow = new SqliteUnitOfWork(factory))
        {
            await uow.BeginTransactionAsync();
            await productRepo.AddTypeAsync(type);
            await uow.RollbackAsync();
        }

        Assert.Empty(await productRepo.ListTypesAsync(includeDisabled: true)); // 回滚后不存在
    }

    [Fact]
    public async Task RecordComplete_WithUnitOfWorkFactory_CommitsAtomically()
    {
        using var env = TestEnv.Create();
        var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, new Pbkdf2PasswordHasher()).InitializeAsync();

        var clock = new SystemClock();
        var audit = new SqliteAuditLog(factory);
        var userRepo = new UserRepository(factory);
        var productRepo = new ProductRepository(factory);
        var pointRepo = new TestPointRepository(factory);
        var configRepo = new ModelPointConfigRepository(factory);
        var paramRepo = new TestParameterRepository(factory);
        var recordRepo = new RecordRepository(factory);
        var uowFactory = new SqliteUnitOfWorkFactory(factory);

        var admin = await userRepo.GetByLoginNameAsync("admin") ?? throw new InvalidOperationException("seed missing");
        var role = await userRepo.GetRoleAsync(admin.RoleId) ?? throw new InvalidOperationException("role missing");
        var actor = new UserContext { UserId = admin.Id, LoginName = admin.LoginName, DisplayName = admin.DisplayName, Role = role };

        var products = new ProductService(productRepo, clock, audit);
        var type = await products.CreateTypeAsync(actor, "压力试验");
        var model = await products.CreateModelAsync(actor, type.Id, "型号1");

        var executors = new Devices.Executors.ExecutorFactory();
        var testPoints = new TestPointService(pointRepo, configRepo, productRepo, executors, clock, audit);
        var point = await testPoints.CreatePointAsync(actor, type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);
        await testPoints.SaveConfigurationAsync(actor, model.Id, new[] { point.Id });

        var parameters = new TestParameterService(paramRepo, productRepo, clock, audit);
        await parameters.SaveProjectAsync(actor, 60);
        await parameters.SaveTypeAsync(actor, type.Id, 5000);
        await parameters.SaveModelAsync(actor, model.Id, 100);

        var execution = new TestExecutionService(recordRepo, productRepo, executors, testPoints, parameters, clock, audit, uowFactory);
        var runtime = await new Devices.DeviceRuntimeFactory(
                await new Infrastructure.Configuration.JsonConfigStore(env.ConfigRoot).LoadAsync<Core.Configuration.DeviceConfig>("device.json"),
                await new Infrastructure.Configuration.JsonConfigStore(env.ConfigRoot).LoadAsync<Core.Configuration.PointsConfig>("points.json"),
                await new Infrastructure.Configuration.JsonConfigStore(env.ConfigRoot).LoadAsync<Core.Configuration.SimulationConfig>("simulation.json"),
                clock)
            .CreateAsync(XXX.TestBench.Core.Domain.Devices.DeviceMode.Simulation);
        await runtime.StartAsync();

        var record = await execution.StartAsync(actor, model.Id, new ProductIdentity("SN001", null, null, null), XXX.TestBench.Core.Domain.Devices.DeviceMode.Simulation, runtime);
        await execution.ExecuteItemAsync(actor, record.Id, point.Id, runtime);
        await execution.CompleteAsync(actor, record.Id);
        await runtime.StopAsync();

        var persistedRecord = await recordRepo.GetRecordAsync(record.Id);
        Assert.NotNull(persistedRecord);
        Assert.Equal(XXX.TestBench.Core.Domain.Records.RecordState.Completed, persistedRecord!.State);
        Assert.NotNull(persistedRecord.ParameterSnapshot);
    }
}
