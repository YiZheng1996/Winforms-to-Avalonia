using XXX.TestBench.App.Composition;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Devices;
using XXX.TestBench.Devices.Drivers;
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

/// <summary>
/// App 测试环境：真实 Infra + 临时 SQLite，用于 ViewModel 行为测试。
/// </summary>
public sealed class AppTestHarness : IDisposable
{
    public string Root { get; }
    public string ConfigRoot { get; }
    public string DataRoot { get; }
    public string DbPath { get; }
    public ShellServices Services { get; }
    public AppComposition? Composition { get; private set; }
    private readonly DeviceConfigurationStore? _configurationStore;

    private AppTestHarness(
        string root,
        ShellServices services,
        string dbPath,
        string dataRoot,
        DeviceConfigurationStore? configurationStore = null)
    {
        Root = root;
        Services = services;
        DbPath = dbPath;
        DataRoot = dataRoot;
        ConfigRoot = Path.Combine(root, "config");
        _configurationStore = configurationStore;
    }

    public static AppTestHarness Create(string deviceMode = "Simulation", bool grouped = false)
    {
        var root = Path.Combine(
            @"D:\Codex相关\设备点位简化改造\test-runs",
            "testbench-app-tests-" + Guid.NewGuid().ToString("N")[..8]);
        var configRoot = Path.Combine(root, "config");
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        WriteConfigs(configRoot, deviceMode, grouped);

        var store = new JsonConfigStore(configRoot);
        var appConfig = store.LoadAsync<AppConfig>("app.json").GetAwaiter().GetResult();
        var deviceConfig = store.LoadAsync<DeviceConfig>("device.json").GetAwaiter().GetResult();
        var pointsConfig = store.LoadAsync<PointsConfig>("points.json").GetAwaiter().GetResult();
        var simulationConfig = store.LoadAsync<SimulationConfig>("simulation.json").GetAwaiter().GetResult();
        appConfig.Validate();
        deviceConfig.Validate();
        pointsConfig.Validate();
        simulationConfig.Validate();

        DeviceConfigurationStore? configurationStore = null;
        if (grouped)
        {
            configurationStore = new DeviceConfigurationStore(configRoot);
            var bootstrap = new DeviceConfigurationBootstrapper(configRoot, store, configurationStore)
                .LoadAsync().GetAwaiter().GetResult();
            deviceConfig = bootstrap.Snapshot.Device;
            pointsConfig = bootstrap.Snapshot.Points;
            simulationConfig = bootstrap.Snapshot.Simulation;
        }

        var clock = new SystemClock();
        var hasher = new Pbkdf2PasswordHasher();
        var dbPath = Path.Combine(dataRoot, appConfig.DefaultPaths.Database);
        var factory = new SqliteConnectionFactory(dbPath);
        new SqliteDatabase(factory, hasher).InitializeAsync().GetAwaiter().GetResult();

        var userRepo = new UserRepository(factory);
        var productRepo = new ProductRepository(factory);
        var recordRepo = new RecordRepository(factory);
        var pointRepo = new TestPointRepository(factory);
        var configRepo = new ModelPointConfigRepository(factory);
        var testParameterRepo = new TestParameterRepository(factory);
        var audit = new SqliteAuditLog(factory);
        var sessions = new InMemorySessionManager();
        var logger = new FileLogger(Path.Combine(dataRoot, "logs"));
        var signalBindings = new SignalBindingsConfig();
        var pressurePoint = pointsConfig.Points.FirstOrDefault(point =>
            string.Equals(point.Code, SimulationPressureExecutor.PressureSignalKey, StringComparison.OrdinalIgnoreCase));
        if (pressurePoint is not null)
            signalBindings.Bindings[SimulationPressureExecutor.PressureSignalKey] = pressurePoint.Id;

        var devicePoints = new DevicePointCatalogService(store, pointsConfig, audit, deviceConfig);
        var devicePointImporter = new DevicePointImporter();
        var devicePointTemplateExporter = new DevicePointTemplateExporter();

        var auth = new AuthenticationService(userRepo, hasher, sessions, clock, audit);
        var executorFactory = new ExecutorFactory();
        var testPoints = new TestPointService(pointRepo, configRepo, productRepo, executorFactory, clock, audit);
        var testParameters = new TestParameterService(testParameterRepo, productRepo, clock, audit);
        var writePipeline = new DeviceWritePipeline(audit, logger);
        var runtimeFactory = new DeviceRuntimeFactory(
            deviceConfig, pointsConfig, simulationConfig, clock,
            signalBindings: signalBindings);
        var deviceModes = new DeviceModeController(runtimeFactory, logger, audit);
        deviceModes.InitializeAsync().GetAwaiter().GetResult();
        if (grouped)
        {
            // 活动快照保持可应用；页面测试额外注入一项不完整草稿，
            // 用来覆盖“未关联通道”只读修复容器的展示行为。
            deviceConfig.Devices.Add(new DeviceConfig.DeviceEntry
            {
                Id = "20000000-0000-0000-0000-000000000003",
                Code = "DEV_ORPHAN",
                Name = "待修复设备",
                ChannelId = "missing-channel",
                DriverKey = DriverKeyCatalog.SiemensS7,
                Model = "S7-1500",
                DeviceMode = DeviceMode.Simulation,
                PollIntervalMs = 500,
                StaleAfterMs = 1500
            });
        }
        DeviceConfigurationService? deviceConfigurations = null;
        if (grouped && configurationStore is not null)
        {
            var coordinator = new DeviceOperationCoordinator();
            deviceConfigurations = new DeviceConfigurationService(
                configurationStore,
                coordinator,
                audit,
                DriverRegistry.CreateDefault().Descriptors,
                hasActiveRun: () => recordRepo.GetActiveRunningRecordAsync().GetAwaiter().GetResult() is not null,
                runtimeFactory: (snapshot, ct) => new DeviceRuntimeFactory(
                    snapshot.Device,
                    snapshot.Points,
                    snapshot.Simulation,
                    clock).CreateAsync(ct),
                currentRuntime: () => deviceModes.Runtime,
                publishRuntime: runtime => deviceModes.PublishStartedRuntimeAsync(runtime));
        }
        var reportRepository = new ReportRepository(factory);
        var execution = new TestExecutionService(recordRepo, productRepo, executorFactory, testPoints, testParameters, clock, audit);
        var reports = new ReportService(recordRepo, productRepo, userRepo, reportRepository, new ClosedXmlReportGenerator(), clock, audit);
        var identityAdministration = new IdentityAdministrationService(userRepo, hasher, clock, audit, new SqliteUnitOfWorkFactory(factory));

        var services = new ShellServices(
            auth, identityAdministration, testPoints, execution, new ProductService(productRepo, clock, audit),
            testParameters, reports, deviceModes, writePipeline,
            recordRepo, pointRepo, configRepo, productRepo, testParameterRepo, userRepo, reportRepository, audit, executorFactory,
            devicePoints, devicePointImporter, devicePointTemplateExporter,
            new DevicePointCatalogExporter(),
            appConfig, deviceConfig, dbPath, "0.1.0");

        if (deviceConfigurations is not null)
            services = services with { DeviceConfigurations = deviceConfigurations, DriverDescriptors = DriverRegistry.CreateDefault().Descriptors.ToList() };
        return new AppTestHarness(root, services, dbPath, dataRoot, configurationStore);
    }

    public static void WriteConfigs(string configRoot, string deviceMode)
        => WriteConfigs(configRoot, deviceMode, grouped: false);

    public static void WriteConfigs(string configRoot, string deviceMode, bool grouped)
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
        if (!grouped)
        {
            File.WriteAllText(Path.Combine(configRoot, "device.json"), $$"""
        {
          "schemaVersion": 3,
          "pollIntervalMs": 500,
          "timeoutMs": 1000,
          "channels": [
            { "id": "10000000-0000-0000-0000-000000000001", "code": "CH_SIM", "name": "仿真设备网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": { "host": "127.0.0.1", "port": 102 } }
          ],
          "devices": [
            { "id": "20000000-0000-0000-0000-000000000001", "code": "DEV_SAMPLE", "name": "SampleDevice", "deviceMode": "{{deviceMode}}", "protocol": "SiemensS7", "address": "127.0.0.1", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500 }
          ]
        }
        """);
            File.WriteAllText(Path.Combine(configRoot, "points.json"), """
        {
          "schemaVersion": 3,
          "groups": [
            { "id": "30000000-0000-0000-0000-000000000001", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 }
          ],
          "points": [
            { "id": "40000000-0000-0000-0000-000000000001", "code": "AI_Pressure", "name": "压力", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000001", "address": "DB1.DBD0", "dataType": "Decimal", "rawDataType": "Decimal", "isWritable": false, "riskLevel": "Normal" },
            { "id": "40000000-0000-0000-0000-000000000002", "code": "DO_Start", "name": "启动", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000001", "address": "DB1.DBX4.0", "dataType": "Boolean", "rawDataType": "Boolean", "writePolicy": "ReadBackEqual", "isWritable": true, "riskLevel": "HighRisk" }
          ]
        }
        """);
            File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
        {
          "schemaVersion": 2,
          "initialValues": {},
          "initialValuesByPointId": {
            "40000000-0000-0000-0000-000000000001": 0.0,
            "40000000-0000-0000-0000-000000000002": false
          },
          "changeRules": [],
          "faultInjectionScenarios": []
        }
        """);
            return;
        }

        File.WriteAllText(Path.Combine(configRoot, "device.json"), $$"""
        {
          "schemaVersion": 3,
          "pollIntervalMs": 500,
          "timeoutMs": 1000,
          "channels": [
            { "id": "10000000-0000-0000-0000-000000000001", "code": "CH_SIM", "name": "仿真设备网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": { "host": "127.0.0.1", "port": 102 } },
            { "id": "10000000-0000-0000-0000-000000000002", "code": "CH_EMPTY", "name": "备用网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": { "host": "127.0.0.1", "port": 103 } }
          ],
          "devices": [
            { "id": "20000000-0000-0000-0000-000000000001", "code": "DEV_A", "name": "设备 A", "deviceMode": "{{deviceMode}}", "protocol": "SiemensS7", "address": "127.0.0.1", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500 },
            { "id": "20000000-0000-0000-0000-000000000002", "code": "DEV_B", "name": "设备 B", "deviceMode": "{{deviceMode}}", "protocol": "SiemensS7", "address": "127.0.0.1", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500 }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "points.json"), """
        {
          "schemaVersion": 3,
          "groups": [
            { "id": "30000000-0000-0000-0000-000000000001", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 },
            { "id": "30000000-0000-0000-0000-000000000002", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "PRESSURE", "name": "压力", "sortOrder": 10 },
            { "id": "30000000-0000-0000-0000-000000000003", "deviceId": "20000000-0000-0000-0000-000000000002", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 },
            { "id": "30000000-0000-0000-0000-000000000004", "deviceId": "20000000-0000-0000-0000-000000000002", "code": "OUTPUT", "name": "输出", "sortOrder": 10 }
          ],
          "points": [
            { "id": "40000000-0000-0000-0000-000000000001", "code": "A_PRESSURE", "name": "入口压力", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000002", "address": "DB1.DBD0", "dataType": "Decimal", "rawDataType": "Decimal", "writePolicy": "ReadBackEqual", "isWritable": false, "riskLevel": "Normal" },
            { "id": "40000000-0000-0000-0000-000000000002", "code": "A_TEMP", "name": "入口温度", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000001", "address": "DB1.DBD4", "dataType": "Float32", "rawDataType": "Float32", "writePolicy": "ReadBackEqual", "isWritable": false, "riskLevel": "Normal" },
            { "id": "40000000-0000-0000-0000-000000000003", "code": "B_START", "name": "启动命令", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000002", "groupId": "30000000-0000-0000-0000-000000000004", "address": "DB1.DBX4.0", "dataType": "Boolean", "rawDataType": "Boolean", "writePolicy": "ReadBackEqual", "isWritable": true, "riskLevel": "HighRisk" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
        {
          "schemaVersion": 2,
          "initialValuesByPointId": {
            "40000000-0000-0000-0000-000000000001": 0.0,
            "40000000-0000-0000-0000-000000000002": 20.0,
            "40000000-0000-0000-0000-000000000003": false
          },
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
