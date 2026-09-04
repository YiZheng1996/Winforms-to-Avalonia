using ClosedXML.Excel;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Devices;
using XXX.TestBench.Devices.Executors;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Reports;
using XXX.TestBench.Infrastructure.Time;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

/// <summary>
/// 阶段 4 集成：Simulation 全闭环（预检→固化参数与项点序列→执行配置序列→判定→完成）+ Excel 报表→重启回读。
/// </summary>
public class Phase4ClosedLoopTests
{
    [Fact]
    public async Task SimulationClosedLoop_WithTemplateReport_PersistsAcrossRestart()
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
        var reportRepo = new ReportRepository(factory);

        var admin = await userRepo.GetByLoginNameAsync("admin") ?? throw new InvalidOperationException("seed missing");
        var role = await userRepo.GetRoleAsync(admin.RoleId) ?? throw new InvalidOperationException("role missing");
        var actor = new UserContext { UserId = admin.Id, LoginName = admin.LoginName, DisplayName = admin.DisplayName, Role = role };

        // 产品类型 → 产品型号 → 试验项点 → 项点配置 → 直编参数
        var products = new ProductService(productRepo, clock, audit);
        var type = await products.CreateTypeAsync(actor, "压力试验");
        var model = await products.CreateModelAsync(actor, type.Id, "型号1");
        var executors = new ExecutorFactory();
        var testPoints = new TestPointService(pointRepo, configRepo, productRepo, executors, clock, audit);
        var point = await testPoints.CreatePointAsync(actor, type.Id, "耐压试验", "PressureExecutor", "PassFail", 1);
        await testPoints.SaveConfigurationAsync(actor, model.Id, new[] { point.Id });

        var parameters = new TestParameterService(paramRepo, productRepo, clock, audit);
        await parameters.SaveProjectAsync(actor, 60);
        await parameters.SaveTypeAsync(actor, type.Id, 5000);
        await parameters.SaveModelAsync(actor, model.Id, 100);

        // 仿真运行时 + 预检启动
        var store = new JsonConfigStore(env.ConfigRoot);
        var deviceConfig = await store.LoadAsync<Core.Configuration.DeviceConfig>("device.json");
        var pointsConfig = await store.LoadAsync<Core.Configuration.PointsConfig>("points.json");
        var simConfig = await store.LoadAsync<Core.Configuration.SimulationConfig>("simulation.json");
        var runtimeFactory = new DeviceRuntimeFactory(deviceConfig, pointsConfig, simConfig, clock);
        await using var runtime = await runtimeFactory.CreateAsync(DeviceMode.Simulation);
        await runtime.StartAsync();

        var execution = new TestExecutionService(recordRepo, productRepo, executors, testPoints, parameters, clock, audit);
        var record = await execution.StartAsync(actor, model.Id, new ProductIdentity("SN001", "B001", "S1", null), DeviceMode.Simulation, runtime);
        var itemResult = await execution.ExecuteItemAsync(actor, record.Id, point.Id, runtime);
        var conclusion = await execution.CompleteAsync(actor, record.Id);

        Assert.Equal(ItemResultState.Passed, itemResult.State);
        Assert.Equal("全部通过（1/1）", conclusion);
        Assert.NotNull(record.ParameterSnapshot);
        Assert.NotNull(record.SequenceSnapshot);

        // 报表：先生成模板，再从已保存数据生成
        var templatePath = Path.Combine(env.DataRoot, "templates", "标准报表.xlsx");
        ClosedXmlReportGenerator.CreateStandardTemplate(templatePath);
        var outputDir = Path.Combine(env.DataRoot, "reports");
        var reportService = new ReportService(recordRepo, productRepo, userRepo, reportRepo, new ClosedXmlReportGenerator(), clock, audit);
        var report = await reportService.GenerateAsync(actor, record.Id, templatePath, outputDir);

        Assert.Equal(XXX.TestBench.Core.Domain.Reports.ReportStatus.Completed, report.Status);
        Assert.True(File.Exists(report.OutputPath));

        // 重启回读
        var factory2 = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory2, hasher).InitializeAsync();
        var recordRepo2 = new RecordRepository(factory2);
        var reloaded = await recordRepo2.GetRecordAsync(record.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(RecordState.Completed, reloaded!.State);
        Assert.Equal("SN001", reloaded.ProductIdentity.ProductNumber);
        Assert.NotNull(reloaded.SequenceSnapshot);
    }
}
