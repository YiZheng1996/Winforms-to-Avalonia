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
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private readonly DeviceOperationCoordinator _deviceOperations;
    private readonly SqliteConnectionFactory _connectionFactory;

    private AppTestHarness(
        string root,
        ShellServices services,
        string dbPath,
        string dataRoot,
        SqliteConnectionFactory connectionFactory,
        DeviceConfigurationStore? configurationStore = null,
        DeviceOperationCoordinator? deviceOperations = null)
    {
        Root = root;
        Services = services;
        DbPath = dbPath;
        DataRoot = dataRoot;
        _connectionFactory = connectionFactory;
        ConfigRoot = Path.Combine(root, "config");
        _configurationStore = configurationStore;
        _deviceOperations = deviceOperations ?? new DeviceOperationCoordinator();
    }

    public static AppTestHarness Create(string deviceMode = "Simulation", bool grouped = false)
    {
        var root = Path.Combine(
            @"D:\Codex相关\compatibility-cleanup-20260928\test-runs",
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
        var signalBindings = store.LoadAsync<SignalBindingsConfig>("signal-bindings.json").GetAwaiter().GetResult();
        var executorFactory = new ExecutorFactory();
        var requiredSignals = executorFactory.Codes
            .SelectMany(code => executorFactory.Get(code).RequiredSignals)
            .ToArray();
        appConfig.Validate();
        deviceConfig.Validate();
        pointsConfig.Validate();
        simulationConfig.Validate();

        DeviceConfigurationStore? configurationStore = null;
        if (grouped)
        {
            configurationStore = new DeviceConfigurationStore(configRoot);
            var registry = DriverRegistry.CreateDefault();
            var bootstrap = new DeviceConfigurationBootstrapper(
                    configRoot, store, configurationStore, registry.Descriptors, requiredSignals)
                .LoadAsync().GetAwaiter().GetResult();
            deviceConfig = bootstrap.Snapshot.Device;
            pointsConfig = bootstrap.Snapshot.Points;
            simulationConfig = bootstrap.Snapshot.Simulation;
            signalBindings = bootstrap.Snapshot.SignalBindings;
        }

        var clock = new SystemClock();
        var hasher = new Pbkdf2PasswordHasher();
        var dbPath = Path.Combine(dataRoot, appConfig.DefaultPaths.Database);
        var factory = new SqliteConnectionFactory(dbPath);
        new SqliteDatabase(factory, hasher)
            .CreateNewAsync(Guid.NewGuid().ToString("D").ToLowerInvariant(), "admin123")
            .GetAwaiter().GetResult();

        var userRepo = new UserRepository(factory);
        var productRepo = new ProductRepository(factory);
        var recordRepo = new RecordRepository(factory);
        var pointRepo = new TestPointRepository(factory);
        var configRepo = new ModelPointConfigRepository(factory);
        var testParameterRepo = new TestParameterRepository(factory);
        var audit = new SqliteAuditLog(factory);
        var sessions = new InMemorySessionManager();
        var logger = new FileLogger(Path.Combine(dataRoot, "logs"));
        var devicePoints = new DevicePointCatalogService(pointsConfig);
        var devicePointImporter = new DevicePointImporter();
        var devicePointTemplateExporter = new DevicePointTemplateExporter();

        var auth = new AuthenticationService(userRepo, hasher, sessions, clock, audit);
        var testPoints = new TestPointService(pointRepo, configRepo, productRepo, executorFactory, clock, audit);
        var testParameters = new TestParameterService(testParameterRepo, productRepo, clock, audit);
        var coordinator = new DeviceOperationCoordinator();
        const string benchId = "app-test-bench";
        var writePipeline = new DeviceWritePipeline(audit, logger, coordinator);
        var runtimeSnapshot = new DeviceConfigurationSnapshot
        {
            Revision = "app-test-initial",
            Device = deviceConfig,
            Points = pointsConfig,
            Simulation = simulationConfig,
            SignalBindings = signalBindings
        };
        var runtimeFactory = new DeviceRuntimeFactory(runtimeSnapshot, clock, requiredSignals: requiredSignals);
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
            deviceConfigurations = new DeviceConfigurationService(
                configurationStore,
                coordinator,
                audit,
                DriverRegistry.CreateDefault().Descriptors,
                hasActiveRun: () => recordRepo.GetActiveRunningRecordAsync(benchId).GetAwaiter().GetResult() is not null,
                runtimeFactory: (snapshot, ct) => new DeviceRuntimeFactory(
                    snapshot, clock, requiredSignals: requiredSignals).CreateAsync(ct),
                currentRuntime: () => deviceModes.Runtime,
                publishRuntime: runtime => deviceModes.PublishStartedRuntimeAsync(runtime),
                requiredSignals: requiredSignals,
                markRuntimeFaulted: error => deviceModes.MarkRuntimeFaulted(error));
        }
        var reportRepository = new ReportRepository(factory);
        var identityAdministration = new IdentityAdministrationService(userRepo, hasher, clock, audit, new SqliteUnitOfWorkFactory(factory));
        var manualOperationGuard = new DeviceManualOperationGuard(coordinator, recordRepo, benchId);
        var diagnosticReader = new DevicePointDiagnosticReader(
            manualOperationGuard,
            () => deviceModes.Runtime,
            logger);

        var services = new ShellServices(
            auth, identityAdministration, testPoints, new ProductService(productRepo, clock, audit),
            testParameters, deviceModes, writePipeline,
            recordRepo, pointRepo, configRepo, productRepo, testParameterRepo, userRepo, reportRepository, audit, executorFactory,
            devicePoints, devicePointImporter, devicePointTemplateExporter,
            new DevicePointCatalogExporter(),
            appConfig, deviceConfig, benchId, dbPath, "0.1.0");

        services = services with
        {
            DeviceConfigurations = deviceConfigurations,
            DriverDescriptors = grouped ? DriverRegistry.CreateDefault().Descriptors.ToList() : services.DriverDescriptors,
            ManualOperationGuard = manualOperationGuard,
            DiagnosticReader = diagnosticReader
        };
        return new AppTestHarness(root, services, dbPath, dataRoot, factory, configurationStore, coordinator);
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

        if (!Enum.TryParse<DeviceMode>(deviceMode, ignoreCase: true, out var mode)
            || mode is not (DeviceMode.Simulation or DeviceMode.Hardware))
            throw new ArgumentException($"测试设备模式非法：{deviceMode}", nameof(deviceMode));

        const string channelA = "10000000-0000-0000-0000-000000000001";
        const string channelB = "10000000-0000-0000-0000-000000000002";
        const string deviceA = "20000000-0000-0000-0000-000000000001";
        const string deviceB = "20000000-0000-0000-0000-000000000002";
        const string groupDefaultA = "30000000-0000-0000-0000-000000000001";
        const string groupPressureA = "30000000-0000-0000-0000-000000000002";
        const string groupDefaultB = "30000000-0000-0000-0000-000000000003";
        const string groupOutputB = "30000000-0000-0000-0000-000000000004";
        const string pointPressureA = "40000000-0000-0000-0000-000000000001";
        const string pointTemperatureA = "40000000-0000-0000-0000-000000000002";
        const string pointStartB = "40000000-0000-0000-0000-000000000003";
        const string pointPressure = "40000000-0000-0000-0000-000000000011";
        const string pointStart = "40000000-0000-0000-0000-000000000012";

        var channels = new List<ChannelEntry>
        {
            new() { Id = channelA, Code = "CH_SIM", Name = "仿真设备网络通道", TransportKind = ChannelTransportKind.Tcp, Enabled = true, TimeoutMs = 1000, Tcp = new TcpChannelParameters() }
        };
        if (grouped)
            channels.Add(new ChannelEntry { Id = channelB, Code = "CH_EMPTY", Name = "备用网络通道", TransportKind = ChannelTransportKind.Tcp, Enabled = true, TimeoutMs = 1000, Tcp = new TcpChannelParameters() });

        DeviceConfig.DeviceEntry MakeDevice(string id, string code, string name, string channelId)
            => new()
            {
                Id = id,
                Code = code,
                Name = name,
                DeviceMode = mode,
                ChannelId = channelId,
                DriverKey = DriverKeyCatalog.SiemensS7,
                Model = "S7-1500",
                PollIntervalMs = 500,
                StaleAfterMs = 1500,
                ScanMode = DeviceScanMode.FixedInterval,
                Timing = new DeviceTimingOptions { ConnectTimeoutMs = 1000, RequestTimeoutMs = 500, RetryCount = 0 },
                AutoDemotion = new DeviceDemotionOptions(),
                SiemensS7 = new SiemensS7ConnectionOptions { Host = "127.0.0.1", Port = 102, Rack = 0, Slot = 0 }
            };

        var devices = grouped
            ? new List<DeviceConfig.DeviceEntry>
            {
                MakeDevice(deviceA, "DEV_A", "设备 A", channelA),
                MakeDevice(deviceB, "DEV_B", "设备 B", channelA)
            }
            : new List<DeviceConfig.DeviceEntry>
            {
                MakeDevice(deviceA, "DEV_SAMPLE", "SampleDevice", channelA)
            };

        var groups = grouped
            ? new List<PointsConfig.PointGroupEntry>
            {
                new() { Id = groupDefaultA, DeviceId = deviceA, Code = "DEFAULT", Name = "未分组", SortOrder = 0 },
                new() { Id = groupPressureA, DeviceId = deviceA, Code = "PRESSURE", Name = "压力", SortOrder = 10 },
                new() { Id = groupDefaultB, DeviceId = deviceB, Code = "DEFAULT", Name = "未分组", SortOrder = 0 },
                new() { Id = groupOutputB, DeviceId = deviceB, Code = "OUTPUT", Name = "输出", SortOrder = 10 }
            }
            : new List<PointsConfig.PointGroupEntry>
            {
                new() { Id = groupDefaultA, DeviceId = deviceA, Code = "DEFAULT", Name = "未分组", SortOrder = 0 }
            };

        var points = grouped
            ? new List<PointsConfig.PointEntry>
            {
                new() { Id = pointPressureA, Code = "A_PRESSURE", Name = "入口压力", Protocol = "SiemensS7", DeviceId = deviceA, GroupId = groupPressureA, Address = "DB1.DBD0", DataType = "Float32", IsWritable = false, RiskLevel = WriteRiskLevel.Normal },
                new() { Id = pointTemperatureA, Code = "A_TEMP", Name = "入口温度", Protocol = "SiemensS7", DeviceId = deviceA, GroupId = groupDefaultA, Address = "DB1.DBD4", DataType = "Float32", IsWritable = false, RiskLevel = WriteRiskLevel.Normal },
                new() { Id = pointStartB, Code = "B_START", Name = "启动命令", Protocol = "SiemensS7", DeviceId = deviceB, GroupId = groupOutputB, Address = "DB1.DBX4.0", DataType = "Bool", WritePolicy = PointWritePolicy.ReadBackEqual, IsWritable = true, RiskLevel = WriteRiskLevel.HighRisk }
            }
            : new List<PointsConfig.PointEntry>
            {
                new() { Id = pointPressure, Code = "AI_Pressure", Name = "压力", Protocol = "SiemensS7", DeviceId = deviceA, GroupId = groupDefaultA, Address = "DB1.DBD0", DataType = "Float32", IsWritable = false, RiskLevel = WriteRiskLevel.Normal },
                new() { Id = pointStart, Code = "DO_Start", Name = "启动", Protocol = "SiemensS7", DeviceId = deviceA, GroupId = groupDefaultA, Address = "DB1.DBX4.0", DataType = "Bool", WritePolicy = PointWritePolicy.ReadBackEqual, IsWritable = true, RiskLevel = WriteRiskLevel.HighRisk }
            };

        var simulation = new SimulationConfig { SchemaVersion = SimulationConfig.CurrentSchemaVersion };
        foreach (var point in points)
            simulation.InitialValuesByPointId[point.Id] = point.DataType == "Bool" ? false : 0.0;

        var bindings = new SignalBindingsConfig();
        bindings.Bindings[SimulationPressureExecutor.PressureSignalKey] = grouped ? pointPressureA : pointPressure;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        WriteJson("device.json", new DeviceConfig { SchemaVersion = DeviceConfig.CurrentSchemaVersion, Channels = channels, Devices = devices });
        WriteJson("points.json", new PointsConfig { SchemaVersion = PointsConfig.CurrentSchemaVersion, Groups = groups, Points = points });
        WriteJson("simulation.json", simulation);
        WriteJson("signal-bindings.json", bindings);

        void WriteJson<T>(string fileName, T value)
            => File.WriteAllText(Path.Combine(configRoot, fileName), JsonSerializer.Serialize(value, options));
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
        try
        {
            Services.DeviceModes.ShutdownAsync().GetAwaiter().GetResult();
        }
        finally
        {
            try
            {
                if (_configurationStore is not null)
                    _configurationStore.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                _deviceOperations.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _connectionFactory.Dispose();
            }
        }
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
