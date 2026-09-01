using XXX.TestBench.App.Composition;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Devices;
using XXX.TestBench.Devices.Executors;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Logging;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Reports;
using XXX.TestBench.Infrastructure.Services;
using XXX.TestBench.Infrastructure.Time;

namespace XXX.TestBench.App.Tests;

/// <summary>App 测试环境：真实 Infra + 临时 SQLite，用于 ViewModel 行为测试。</summary>
public sealed class AppTestHarness : IDisposable
{
    public string Root { get; }
    public string ConfigRoot { get; }
    public string DataRoot { get; }
    public string DbPath { get; }
    public ShellServices Services { get; }
    public AppComposition? Composition { get; private set; }

    private AppTestHarness(string root, ShellServices services, string dbPath, string dataRoot)
    {
        Root = root;
        Services = services;
        DbPath = dbPath;
        DataRoot = dataRoot;
        ConfigRoot = Path.Combine(root, "config");
    }

    public static AppTestHarness Create(string deviceMode = "Simulation")
    {
        var root = Path.Combine(Path.GetTempPath(), "testbench-app-tests-" + Guid.NewGuid().ToString("N")[..8]);
        var configRoot = Path.Combine(root, "config");
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        WriteConfigs(configRoot, deviceMode);

        var store = new JsonConfigStore(configRoot);
        var appConfig = store.LoadAsync<AppConfig>("app.json").GetAwaiter().GetResult();
        var deviceConfig = store.LoadAsync<DeviceConfig>("device.json").GetAwaiter().GetResult();
        var pointsConfig = store.LoadAsync<PointsConfig>("points.json").GetAwaiter().GetResult();
        var simulationConfig = store.LoadAsync<SimulationConfig>("simulation.json").GetAwaiter().GetResult();
        appConfig.Validate();
        deviceConfig.Validate();
        pointsConfig.Validate();
        simulationConfig.Validate();

        var clock = new SystemClock();
        var hasher = new Pbkdf2PasswordHasher();
        var dbPath = Path.Combine(dataRoot, appConfig.DefaultPaths.Database);
        var factory = new SqliteConnectionFactory(dbPath);
        new SqliteDatabase(factory, hasher).InitializeAsync().GetAwaiter().GetResult();

        var userRepo = new UserRepository(factory);
        var productRepo = new ProductRepository(factory);
        var definitionRepo = new TestDefinitionRepository(factory);
        var recipeRepo = new RecipeRepository(factory);
        var taskRepo = new TaskRepository(factory);
        var audit = new SqliteAuditLog(factory);
        var sessions = new InMemorySessionManager();
        var logger = new FileLogger(Path.Combine(dataRoot, "logs"));

        var auth = new AuthenticationService(userRepo, hasher, sessions, clock, audit);
        var recipes = new RecipeService(recipeRepo, productRepo, definitionRepo, clock, audit);
        var tasks = new TaskService(taskRepo, productRepo, recipeRepo, clock, audit);
        var writePipeline = new DeviceWritePipeline(audit, logger);
        var runtimeFactory = new DeviceRuntimeFactory(deviceConfig, pointsConfig, simulationConfig, clock);
        var deviceModes = new DeviceModeController(runtimeFactory, logger, audit);
        deviceModes.InitializeAsync(deviceConfig.DeviceMode).GetAwaiter().GetResult();
        var reportRepository = new ReportRepository(factory);
        var execution = new TestExecutionService(taskRepo, recipeRepo, definitionRepo, new ExecutorFactory(), tasks, clock, audit);
        var reports = new ReportService(taskRepo, recipeRepo, productRepo, definitionRepo, userRepo, reportRepository, new ClosedXmlReportGenerator(), clock, audit);

        var services = new ShellServices(
            auth, tasks, execution, recipes, new ProductService(productRepo, clock, audit),
            new TestDefinitionService(definitionRepo, clock, audit), reports, deviceModes, writePipeline,
            taskRepo, recipeRepo, productRepo, definitionRepo, userRepo, reportRepository, audit,
            appConfig, deviceConfig, dbPath, "0.1.0");

        return new AppTestHarness(root, services, dbPath, dataRoot);
    }

    public static void WriteConfigs(string configRoot, string deviceMode)
    {
        File.WriteAllText(Path.Combine(configRoot, "app.json"), """
        {
          "schemaVersion": 1,
          "systemName": "测试系统",
          "brand": "XXX.TestBench.Template",
          "language": "zh-CN",
          "modules": { "processMonitor": true, "deviceCalibration": true },
          "defaultPaths": { "database": "testbench.db", "reportOutput": "reports", "reportTemplates": "assets/report-templates" }
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "device.json"), $$"""
        {
          "schemaVersion": 1,
          "deviceMode": "{{deviceMode}}",
          "pollIntervalMs": 500,
          "timeoutMs": 1000,
          "devices": [ { "name": "SampleDevice", "protocol": "Simulation", "address": "sim://sample", "enabled": true } ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "points.json"), """
        {
          "schemaVersion": 1,
          "points": [
            { "code": "AI_Pressure", "protocol": "Simulation", "address": "sim.pressure", "dataType": "Decimal", "unit": "MPa", "isWritable": false, "riskLevel": "Normal" },
            { "code": "DO_Start", "protocol": "Simulation", "address": "sim.start", "dataType": "Boolean", "isWritable": true, "riskLevel": "HighRisk" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
        {
          "schemaVersion": 1,
          "initialValues": { "sim.pressure": 0.0, "sim.start": false },
          "changeRules": [],
          "faultInjectionScenarios": []
        }
        """);
    }

    public async Task<UserContext> AdminActorAsync()
    {
        var admin = await Services.UserRepository.GetByLoginNameAsync("admin");
        var role = await Services.UserRepository.GetRoleAsync(admin!.RoleId);
        return new UserContext { UserId = admin.Id, LoginName = admin.LoginName, DisplayName = admin.DisplayName, Role = role! };
    }

    public async Task AddOperatorUserAsync(string loginName, string password)
    {
        var roles = await Services.UserRepository.ListRolesAsync();
        var opRole = roles.First(r => r.Name == "Operator");
        var hasher = new Pbkdf2PasswordHasher();
        await Services.UserRepository.AddAsync(new User
        {
            LoginName = loginName,
            DisplayName = "操作员",
            PasswordHash = hasher.Hash(password),
            MustChangePassword = false,
            RoleId = opRole.Id,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
