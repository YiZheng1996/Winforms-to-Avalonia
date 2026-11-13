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
/// 产品配置链路与重启回读；新版试验运行闭环待 W06 实现，不能沿用旧流程作为验收证据。
/// </summary>
public class FullLoopTests
{
    [Fact]
    public async Task ProductConfiguration_PersistsAndSurvivesRestart()
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

        // 直编参数：项目级 + 产品类型/产品型号组合级
        var parameters = new TestParameterService(paramRepo, productRepo, clock, audit);
        await parameters.SaveProjectAsync(actor, 60);
        await parameters.SaveProductAsync(actor, type.Id, model.Id, 5000, 100);
        var effective = await parameters.LoadEffectiveAsync(model.Id);
        Assert.Equal(60, effective.TestTimeSeconds);
        Assert.Equal(5000, effective.TestVoltageV);
        Assert.Equal(100, effective.ProtectCurrentMa);

        // 试验项点 + 项点配置（产品类型→项点，型号→序列）
        var executors = new Devices.Executors.ExecutorFactory();
        var testPoints = new TestPointService(pointRepo, configRepo, productRepo, executors, clock, audit);
        var point = await testPoints.CreatePointAsync(actor, type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);
        await testPoints.SaveConfigurationAsync(actor, model.Id, new[] { point.Id }, model.SequenceRevision);
        var sequence = await testPoints.GetSequenceAsync(model.Id);
        Assert.Single(sequence);
        Assert.Equal(point.Id, sequence[0].Id);

        // 重启回读
        var factory2 = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory2, hasher).InitializeAsync();
        var productRepo2 = new ProductRepository(factory2);
        Assert.Equal(model.Name, (await productRepo2.GetModelAsync(model.Id))?.Name);
        var parameters2 = new TestParameterService(new TestParameterRepository(factory2), productRepo2, clock, audit);
        Assert.Equal(effective, await parameters2.LoadEffectiveAsync(model.Id));
        Assert.Empty(await new RecordRepository(factory2).ListRecordsAsync(null));

        // 任务表已不存在
        var taskTableCount = Convert.ToInt64(await factory2.Db.Ado.ExecuteScalarAsync(
            "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='test_tasks'", new { }, default));
        Assert.Equal(0L, taskTableCount);
    }
}
