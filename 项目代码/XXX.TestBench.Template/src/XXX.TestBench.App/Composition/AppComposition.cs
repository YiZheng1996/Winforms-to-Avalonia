using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
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

namespace XXX.TestBench.App.Composition;

/// <summary>
/// 组合根：创建具体实现；View/ViewModel 不通过全局服务定位器获取依赖。
/// </summary>
public sealed class AppComposition
{
    private AppComposition() { }

    public IAppLogger Logger { get; private set; } = null!;
    public ShellViewModel? Shell { get; private set; }
    public string? StartupError { get; private set; }
    public AuthenticationService? Authentication { get; private set; }
    public DeviceModeController? DeviceModes { get; private set; }
    public SqliteDatabase? Database { get; private set; }
    public ISqliteConnectionFactory? ConnectionFactory { get; private set; }
    public TestExecutionService? TestExecution { get; private set; }
    public ReportService? Reports { get; private set; }

    public static AppComposition Create(string configRoot, string dataRoot)
    {
        var composition = new AppComposition();
        Task.Run(() => composition.Initialize(configRoot, dataRoot)).GetAwaiter().GetResult();
        return composition;
    }

    private void Initialize(string configRoot, string dataRoot)
    {
        var logDir = Path.Combine(dataRoot, "logs");
        Logger = new FileLogger(logDir);

        try
        {
            var store = new JsonConfigStore(configRoot);
            var appConfig = store.LoadAsync<AppConfig>("app.json").GetAwaiter().GetResult();
            var deviceConfig = store.LoadAsync<DeviceConfig>("device.json").GetAwaiter().GetResult();
            var pointsConfig = store.LoadAsync<PointsConfig>("points.json").GetAwaiter().GetResult();
            var simulationConfig = store.LoadAsync<SimulationConfig>("simulation.json").GetAwaiter().GetResult();
            ConfigurationValidator.ValidateAll(appConfig, deviceConfig, pointsConfig, simulationConfig);

            var clock = new SystemClock();
            var hasher = new Pbkdf2PasswordHasher();
            var dbPath = Path.GetFullPath(Path.Combine(dataRoot, appConfig.DefaultPaths.Database));
            var factory = new SqliteConnectionFactory(dbPath);
            var database = new SqliteDatabase(factory, hasher);
            database.InitializeAsync().GetAwaiter().GetResult();

            var userRepo = new UserRepository(factory);
            var productRepo = new ProductRepository(factory);
            var definitionRepo = new TestDefinitionRepository(factory);
            var testParameterRepo = new TestParameterRepository(factory);
            var taskRepo = new TaskRepository(factory);
            var audit = new SqliteAuditLog(factory);
            var sessions = new InMemorySessionManager();
            var unitOfWork = new SqliteUnitOfWork(factory);

            Authentication = new AuthenticationService(userRepo, hasher, sessions, clock, audit);
            var uowFactory = new SqliteUnitOfWorkFactory(factory);
            var testParameters = new TestParameterService(testParameterRepo, productRepo, clock, audit);
            var tasks = new TaskService(taskRepo, productRepo, clock, audit, uowFactory);
            var writePipeline = new DeviceWritePipeline(audit, Logger);
            var runtimeFactory = new DeviceRuntimeFactory(deviceConfig, pointsConfig, simulationConfig, clock);
            DeviceModes = new DeviceModeController(runtimeFactory, Logger, audit);
            DeviceModes.InitializeAsync(deviceConfig.DeviceMode).GetAwaiter().GetResult();
            var executorFactory = new ExecutorFactory();
            var reportRepository = new ReportRepository(factory);
            var reportGenerator = new ClosedXmlReportGenerator();
            TestExecution = new TestExecutionService(taskRepo, definitionRepo, executorFactory, tasks, testParameters, clock, audit, uowFactory);
            Reports = new ReportService(taskRepo, productRepo, definitionRepo, userRepo, reportRepository, reportGenerator, clock, audit);

            var services = new ShellServices(
                Authentication,
                tasks,
                TestExecution,
                new ProductService(productRepo, clock, audit),
                testParameters,
                Reports,
                DeviceModes,
                writePipeline,
                taskRepo,
                productRepo,
                definitionRepo,
                testParameterRepo,
                userRepo,
                reportRepository,
                audit,
                appConfig,
                deviceConfig,
                dbPath,
                typeof(AppComposition).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

            Shell = new ShellViewModel(services);
        }
        catch (Exception ex)
        {
            StartupError = ex is ConfigValidationException or DomainException ? ex.Message : ex.ToString();
            Logger.Error("启动失败", ex);
            Shell = new ShellViewModel(null, isFaulted: true, faultMessage: StartupError);
        }
    }
}
