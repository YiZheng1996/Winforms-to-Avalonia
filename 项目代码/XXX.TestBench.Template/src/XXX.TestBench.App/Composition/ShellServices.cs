using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.Composition;

/// <summary>
/// 组合根显式注入的服务集合；ViewModel 不通过全局服务定位器获取依赖。
/// </summary>
public sealed record ShellServices(
    AuthenticationService Authentication,
    IdentityAdministrationService IdentityAdministration,
    TestPointService TestPoints,
    TestExecutionService Execution,
    ProductService Products,
    TestParameterService Parameters,
    ReportService Reports,
    DeviceModeController DeviceModes,
    DeviceWritePipeline WritePipeline,
    IRecordRepository RecordRepository,
    ITestPointRepository TestPointRepository,
    IModelPointConfigRepository ModelPointConfigRepository,
    IProductRepository ProductRepository,
    ITestParameterRepository TestParameterRepository,
    IUserRepository UserRepository,
    IReportRepository ReportRepository,
    IAuditLog AuditLog,
    ITestItemExecutorFactory Executors,
    DevicePointCatalogService DevicePoints,
    IDevicePointImporter DevicePointImporter,
    IDevicePointTemplateExporter DevicePointTemplateExporter,
    IDevicePointCatalogExporter DevicePointCatalogExporter,
    AppConfig AppConfig,
    DeviceConfig DeviceConfig,
    string DatabasePath,
    string Version,
    DeviceConfigurationService? DeviceConfigurations = null,
    IReadOnlyList<IDeviceDriverDescriptor>? DriverDescriptors = null,
    IDeviceConnectionTester? DeviceConnectionTester = null,
    IDeviceEventSink? DeviceEvents = null);
