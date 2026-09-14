using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices;
using XXX.TestBench.Devices.Executors;
using XXX.TestBench.Devices.Drivers;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Logging;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Reports;
using XXX.TestBench.Infrastructure.Services;
using XXX.TestBench.Infrastructure.Time;
using XXX.TestBench.Devices.Runtime;
using XXX.TestBench.Devices.Siemens;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.App.Composition;

/// <summary>
/// 组合根：创建具体实现；View/ViewModel 不通过全局服务定位器获取依赖。
/// </summary>
public sealed class AppComposition
{
    /// <summary>
    /// 私有构造，防止外部直接创建。
    /// </summary>
    private AppComposition() { }

    /// <summary>
    /// 应用日志。
    /// </summary>
    public IAppLogger Logger { get; private set; } = null!;
    /// <summary>
    /// 主界面视图模型。
    /// </summary>
    public ShellViewModel? Shell { get; private set; }
    /// <summary>
    /// 启动失败时的错误信息。
    /// </summary>
    public string? StartupError { get; private set; }
    /// <summary>
    /// 登录服务。
    /// </summary>
    public AuthenticationService? Authentication { get; private set; }
    /// <summary>
    /// 设备模式控制器。
    /// </summary>
    public DeviceModeController? DeviceModes { get; private set; }
    /// <summary>
    /// 数据库初始化器。
    /// </summary>
    public SqliteDatabase? Database { get; private set; }
    /// <summary>
    /// 数据库连接工厂。
    /// </summary>
    public ISqliteConnectionFactory? ConnectionFactory { get; private set; }
    /// <summary>
    /// 试验执行服务。
    /// </summary>
    public TestExecutionService? TestExecution { get; private set; }
    /// <summary>
    /// 报表服务。
    /// </summary>
    public ReportService? Reports { get; private set; }
    /// <summary>
    /// 设备配置应用和试验/写入共用的操作门。
    /// </summary>
    public DeviceOperationCoordinator? DeviceOperations { get; private set; }
    /// <summary>
    /// 完整设备配置快照服务。
    /// </summary>
    public DeviceConfigurationService? DeviceConfigurations { get; private set; }

    /// <summary>
    /// 创建并完成全部依赖装配。
    /// </summary>
    public static AppComposition Create(string configRoot, string dataRoot)
    {
        var composition = new AppComposition();
        Task.Run(() => composition.Initialize(configRoot, dataRoot)).GetAwaiter().GetResult();
        return composition;
    }

    /// <summary>
    /// 装配配置、数据库、仓库与服务并创建主视图模型。
    /// </summary>
    private void Initialize(string configRoot, string dataRoot)
    {
        var logDir = Path.Combine(dataRoot, "logs");
        Logger = new FileLogger(logDir);

        try
        {
            // 加载并校验四份配置。
            var store = new JsonConfigStore(configRoot);
            var appConfig = store.LoadAsync<AppConfig>("app.json").GetAwaiter().GetResult();
            appConfig.Validate();
            var driverRegistry = DriverRegistry.CreateDefault();
            var executorFactory = new ExecutorFactory();
            var deviceConfigurationStore = new DeviceConfigurationStore(configRoot);
            var bootstrapper = new DeviceConfigurationBootstrapper(configRoot, store, deviceConfigurationStore);
            var bootstrap = bootstrapper.LoadAsync().GetAwaiter().GetResult();
            var snapshot = bootstrap.Snapshot;
            var deviceConfig = snapshot.Device;
            var pointsConfig = snapshot.Points;
            var simulationConfig = snapshot.Simulation;
            MultiDeviceConfigurationValidator.EnsureValid(
                snapshot,
                driverRegistry.Descriptors,
                executorFactory.Codes.SelectMany(code => executorFactory.Get(code).RequiredSignals).ToList());

            // 创建数据库并执行初始化。
            var clock = new SystemClock();
            var hasher = new Pbkdf2PasswordHasher();
            var dbPath = Path.GetFullPath(Path.Combine(dataRoot, appConfig.DefaultPaths.Database));
            var factory = new SqliteConnectionFactory(dbPath);
            var database = new SqliteDatabase(factory, hasher);
            database.InitializeAsync().GetAwaiter().GetResult();

            // 创建仓储与基础设施服务。
            var userRepo = new UserRepository(factory);
            var productRepo = new ProductRepository(factory);
            var recordRepo = new RecordRepository(factory);
            var pointRepo = new TestPointRepository(factory);
            var configRepo = new ModelPointConfigRepository(factory);
            var testParameterRepo = new TestParameterRepository(factory);
            var audit = new SqliteAuditLog(factory);
            var sessions = new InMemorySessionManager();
            var unitOfWork = new SqliteUnitOfWork(factory);
            DeviceOperations = new DeviceOperationCoordinator();
            var s7ClientFactory = new S7NetPlusPlcClientFactory();
            var modbusClientFactory = new NModbusClientFactory();
            var deviceEvents = new InMemoryDeviceEventHub();
            var connectionTester = new CompositeDeviceConnectionTester(new[]
            {
                (DriverKeyCatalog.SiemensS7, (IDeviceConnectionTester)new S7DeviceConnectionTester(s7ClientFactory, DeviceOperations)),
                (DriverKeyCatalog.ModbusTcp, (IDeviceConnectionTester)new ModbusTcpConnectionTester(modbusClientFactory, DeviceOperations)),
                (DriverKeyCatalog.ModbusRtu, (IDeviceConnectionTester)new ModbusRtuConnectionTester(
                    modbusClientFactory,
                    DeviceOperations,
                    port => DeviceModes?.Runtime is MultiDeviceRuntime runtime
                        && runtime.IsSerialPortLeased(port)))
            });
            DeviceConfigurations = new DeviceConfigurationService(
                deviceConfigurationStore,
                DeviceOperations,
                audit,
                driverRegistry.Descriptors,
                hasActiveRun: () => recordRepo.GetActiveRunningRecordAsync().GetAwaiter().GetResult() is not null,
                runtimeFactory: (candidate, ct) => new DeviceRuntimeFactory(
                    candidate.Device, candidate.Points, candidate.Simulation, clock, candidate.Revision,
                    candidate.SignalBindings, s7ClientFactory, deviceEvents, modbusClientFactory)
                    .CreateAsync(ct),
                currentRuntime: () => DeviceModes?.Runtime,
                publishRuntime: runtime => DeviceModes is null
                    ? Task.FromException(new InvalidOperationException("设备模式控制器尚未创建"))
                    : DeviceModes.PublishStartedRuntimeAsync(runtime),
                requiredSignals: executorFactory.Codes
                    .SelectMany(code => executorFactory.Get(code).RequiredSignals)
                    .ToList(),
                events: deviceEvents,
                markRuntimeFaulted: error => DeviceModes?.MarkRuntimeFaulted(error));
            var devicePoints = new DevicePointCatalogService(store, pointsConfig, audit, deviceConfig);
            var devicePointImporter = new DevicePointImporter();
            var devicePointTemplateExporter = new DevicePointTemplateExporter();
            var devicePointCatalogExporter = new DevicePointCatalogExporter();

            // 创建业务服务并组装主视图模型。
            Authentication = new AuthenticationService(userRepo, hasher, sessions, clock, audit);
            var uowFactory = new SqliteUnitOfWorkFactory(factory);
            var identityAdministration = new IdentityAdministrationService(userRepo, hasher, clock, audit, uowFactory);
            var testParameters = new TestParameterService(testParameterRepo, productRepo, clock, audit);
            var testPoints = new TestPointService(pointRepo, configRepo, productRepo, executorFactory, clock, audit);
            var writePipeline = new DeviceWritePipeline(audit, Logger, DeviceOperations);
            var runtimeFactory = new DeviceRuntimeFactory(
                deviceConfig, pointsConfig, simulationConfig, clock, snapshot.Revision, snapshot.SignalBindings,
                s7ClientFactory, deviceEvents, modbusClientFactory);
            DeviceModes = new DeviceModeController(runtimeFactory, Logger, audit);
            DeviceModes.InitializeAsync().GetAwaiter().GetResult();

            var reportRepository = new ReportRepository(factory);
            var reportGenerator = new ClosedXmlReportGenerator();
            TestExecution = new TestExecutionService(recordRepo, productRepo, executorFactory, testPoints, testParameters, clock, audit, uowFactory, DeviceOperations);
            Reports = new ReportService(recordRepo, productRepo, userRepo, reportRepository, reportGenerator, clock, audit);

            var services = new ShellServices(
                Authentication,
                identityAdministration,
                testPoints,
                TestExecution,
                new ProductService(productRepo, clock, audit),
                testParameters,
                Reports,
                DeviceModes,
                writePipeline,
                recordRepo,
                pointRepo,
                configRepo,
                productRepo,
                testParameterRepo,
                userRepo,
                reportRepository,
                audit,
                executorFactory,
                devicePoints,
                devicePointImporter,
                devicePointTemplateExporter,
                devicePointCatalogExporter,
                appConfig,
                deviceConfig,
                dbPath,
                typeof(AppComposition).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                DeviceConfigurations,
                driverRegistry.Descriptors.ToList(),
                connectionTester,
                deviceEvents);

            Shell = new ShellViewModel(services);
        }
        // 启动失败进入故障页，不静默回退。
        catch (Exception ex)
        {
            StartupError = ex is ConfigValidationException or DomainException ? ex.Message : ex.ToString();
            Logger.Error("启动失败", ex);
            Shell = new ShellViewModel(null, isFaulted: true, faultMessage: StartupError);
        }
    }
}
