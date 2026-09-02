using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
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
/// 全链路：产品类型→产品型号→试验项点→项点配置→直编参数→启动(Simulation，固化快照)→完成→重启回读。
/// </summary>
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
        var pointRepo = new TestPointRepository(factory);
        var configRepo = new ModelPointConfigRepository(factory);
        var paramRepo = new TestParameterRepository(factory);
        var recordRepo = new RecordRepository(factory);

        var admin = await userRepo.GetByLoginNameAsync("admin") ?? throw new InvalidOperationException("admin seed missing");
        var role = await userRepo.GetRoleAsync(admin.RoleId) ?? throw new InvalidOperationException("role missing");
        var actor = new UserContext { UserId = admin.Id, LoginName = admin.LoginName, DisplayName = admin.DisplayName, Role = role };

        // 主数据：产品类型/型号
        var type = new ProductType { Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        await productRepo.AddTypeAsync(type);
        var model = new ProductModel { ProductTypeId = type.Id, Name = "型号1", CreatedAtUtc = clock.UtcNow };
        await productRepo.AddModelAsync(model);

        // 直编参数：项目/类型/型号三级
        var parameters = new TestParameterService(paramRepo, productRepo, clock, audit);
        await parameters.SaveProjectAsync(actor, 60);
        await parameters.SaveTypeAsync(actor, type.Id, 5000);
        await parameters.SaveModelAsync(actor, model.Id, 100);
        var effective = await parameters.LoadEffectiveAsync(model.Id);
        Assert.Equal(60, effective.TestTimeSeconds);
        Assert.Equal(5000, effective.TestVoltageV);
        Assert.Equal(100, effective.ProtectCurrentMa);

        // 试验项点 + 项点配置（产品类型→项点，型号→序列）
        var executors = new Devices.Executors.ExecutorFactory();
        var testPoints = new TestPointService(pointRepo, configRepo, productRepo, executors, clock, audit);
        var point = await testPoints.CreatePointAsync(actor, type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);
        await testPoints.SaveConfigurationAsync(actor, model.Id, new[] { point.Id });
        var sequence = await testPoints.GetSequenceAsync(model.Id);
        Assert.Single(sequence);
        Assert.Equal(point.Id, sequence[0].Id);

        // 启动试验（固化参数与序列快照）→ 执行 → 完成
        var execution = new TestExecutionService(recordRepo, productRepo, executors, testPoints, parameters, clock, audit);
        var runtime = await new Devices.DeviceRuntimeFactory(
                await new Infrastructure.Configuration.JsonConfigStore(env.ConfigRoot).LoadAsync<Core.Configuration.DeviceConfig>("device.json"),
                await new Infrastructure.Configuration.JsonConfigStore(env.ConfigRoot).LoadAsync<Core.Configuration.PointsConfig>("points.json"),
                await new Infrastructure.Configuration.JsonConfigStore(env.ConfigRoot).LoadAsync<Core.Configuration.SimulationConfig>("simulation.json"),
                clock)
            .CreateAsync(DeviceMode.Simulation);
        await runtime.StartAsync();

        var record = await execution.StartAsync(actor, model.Id, new ProductIdentity("SN001", "B2026", "S1", "测试"), DeviceMode.Simulation, runtime);
        Assert.NotNull(record.ParameterSnapshot);
        Assert.NotNull(record.SequenceSnapshot);
        var itemResult = await execution.ExecuteItemAsync(actor, record.Id, point.Id, runtime);
        await execution.CompleteAsync(actor, record.Id);
        Assert.Equal(ItemResultState.Passed, itemResult.State);
        await runtime.StopAsync();

        // 重启回读
        var factory2 = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory2, hasher).InitializeAsync();
        var recordRepo2 = new RecordRepository(factory2);
        var reloaded = await recordRepo2.GetRecordAsync(record.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(RecordState.Completed, reloaded!.State);
        Assert.Equal("SN001", reloaded.ProductIdentity.ProductNumber);
        Assert.Equal(record.RecordNumber, reloaded.RecordNumber);

        var recordCount = Convert.ToInt64(await factory2.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM test_records WHERE record_number=@recordNumber",
            new { recordNumber = record.RecordNumber }, default));
        Assert.Equal(1L, recordCount);

        // 任务表已不存在
        var taskTableCount = Convert.ToInt64(await factory2.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='test_tasks'", new { }, default));
        Assert.Equal(0L, taskTableCount);
    }
}
