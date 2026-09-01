using ClosedXML.Excel;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Domain.TestDefinitions;
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

/// <summary>阶段 4 集成：Simulation 全闭环（预检→执行→判定→完成）+ Excel 报表（模板）→重启回读。</summary>
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
        var defRepo = new TestDefinitionRepository(factory);
        var recipeRepo = new RecipeRepository(factory);
        var taskRepo = new TaskRepository(factory);
        var reportRepo = new ReportRepository(factory);

        var admin = await userRepo.GetByLoginNameAsync("admin") ?? throw new InvalidOperationException("seed missing");
        var role = await userRepo.GetRoleAsync(admin.RoleId) ?? throw new InvalidOperationException("role missing");
        var actor = new UserContext { UserId = admin.Id, LoginName = admin.LoginName, DisplayName = admin.DisplayName, Role = role };

        // 主数据 + 配方（目标电压 0，仿真初值 0 → 通过）
        var products = new ProductService(productRepo, clock, audit);
        var type = await products.CreateTypeAsync(actor, "PT", "压力试验");
        var model = await products.CreateModelAsync(actor, type.Id, "M1", "型号1");
        var definitions = new TestDefinitionService(defRepo, clock, audit);
        var item = await definitions.CreateItemAsync(actor, "IT1", "耐压", "PressureExecutor", "PassFail", 1);
        var voltage = await definitions.CreateParameterAsync(actor, item.Id, "Voltage", "试验电压", ParameterDataType.Decimal, true, "kV", 0, 50, 2, null, 1);
        var recipes = new RecipeService(recipeRepo, productRepo, defRepo, clock, audit);
        var draft = await recipes.CreateDraftAsync(actor, model.Id, "配方1");
        await recipes.AddItemAsync(actor, draft.Id, item.Id, 1);
        var recipeItems = await recipeRepo.ListItemsAsync(draft.Id);
        await recipes.SetParameterValueAsync(actor, draft.Id, recipeItems[0].Id, voltage.Id, "0");
        await recipes.PublishAsync(actor, draft.Id);

        // 任务 + 仿真运行时 + 预检启动
        var tasks = new TaskService(taskRepo, productRepo, recipeRepo, clock, audit);
        var task = await tasks.CreateAsync(actor, model.Id, draft.Id, new ProductIdentity("SN001", "B001", "S1", null));
        await tasks.ToReadyAsync(actor, task.Id);

        var store = new JsonConfigStore(env.ConfigRoot);
        var deviceConfig = await store.LoadAsync<Core.Configuration.DeviceConfig>("device.json");
        var pointsConfig = await store.LoadAsync<Core.Configuration.PointsConfig>("points.json");
        var simConfig = await store.LoadAsync<Core.Configuration.SimulationConfig>("simulation.json");
        var runtimeFactory = new DeviceRuntimeFactory(deviceConfig, pointsConfig, simConfig, clock);
        await using var runtime = await runtimeFactory.CreateAsync(DeviceMode.Simulation);
        await runtime.StartAsync();

        var execution = new TestExecutionService(taskRepo, recipeRepo, defRepo, new ExecutorFactory(), tasks, clock, audit);
        var record = await execution.StartAsync(actor, task.Id, DeviceMode.Simulation, runtime);
        var itemResult = await execution.ExecuteItemAsync(actor, record.Id, recipeItems[0].Id, runtime);
        var conclusion = await execution.CompleteAsync(actor, record.Id);

        Assert.Equal(ItemResultState.Passed, itemResult.State);
        Assert.Equal("全部通过（1/1）", conclusion);

        // 报表：先生成模板，再从已保存数据生成
        var templatePath = Path.Combine(env.DataRoot, "templates", "标准报表.xlsx");
        ClosedXmlReportGenerator.CreateStandardTemplate(templatePath);
        var outputDir = Path.Combine(env.DataRoot, "reports");
        var reportService = new ReportService(taskRepo, recipeRepo, productRepo, defRepo, userRepo, reportRepo, new ClosedXmlReportGenerator(), clock, audit);
        var report = await reportService.GenerateAsync(actor, record.Id, templatePath, outputDir);

        Assert.Equal(ReportStatus.Completed, report.Status);
        Assert.True(File.Exists(report.OutputPath), "报表输出文件应存在");
        using (var workbook = new XLWorkbook(report.OutputPath!))
        {
            var sheet = workbook.Worksheet("报表");
            Assert.Equal(task.TaskNumber, sheet.Cell("C2").GetString());
            Assert.Equal("全部通过（1/1）", sheet.Cell("E5").GetString());
        }

        // 重启回读：任务/记录/项点结果/报表记录
        var factory2 = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory2, hasher).InitializeAsync();
        var taskRepo2 = new TaskRepository(factory2);
        var reportRepo2 = new ReportRepository(factory2);

        var reloadedTask = await taskRepo2.GetAsync(task.Id);
        Assert.Equal(TaskState.Completed, reloadedTask!.State);
        var reloadedRecord = await taskRepo2.GetRecordAsync(record.Id);
        Assert.Equal(RecordState.Completed, reloadedRecord!.State);
        Assert.Single(await taskRepo2.ListItemResultsAsync(record.Id));
        var reloadedReport = await reportRepo2.ListByRecordAsync(record.Id);
        Assert.Single(reloadedReport);
        Assert.Equal(ReportStatus.Completed, reloadedReport[0].Status);
    }
}
