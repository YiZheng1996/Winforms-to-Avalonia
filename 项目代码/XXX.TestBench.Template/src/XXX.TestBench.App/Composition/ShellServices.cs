using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.Composition;

/// <summary>
/// 组合根显式注入的服务集合；ViewModel 不通过全局服务定位器获取依赖。
/// </summary>
public sealed record ShellServices(
    AuthenticationService Authentication,
    TaskService Tasks,
    TestExecutionService Execution,
    RecipeService Recipes,
    ProductService Products,
    TestDefinitionService Definitions,
    TestParameterService Parameters,
    ReportService Reports,
    DeviceModeController DeviceModes,
    DeviceWritePipeline WritePipeline,
    ITaskRepository TaskRepository,
    IRecipeRepository RecipeRepository,
    IProductRepository ProductRepository,
    ITestDefinitionRepository DefinitionRepository,
    ITestParameterRepository TestParameterRepository,
    IUserRepository UserRepository,
    IReportRepository ReportRepository,
    IAuditLog AuditLog,
    AppConfig AppConfig,
    DeviceConfig DeviceConfig,
    string DatabasePath,
    string Version);
