using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Domain.TestParameters;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 项点执行上下文：固定序列项点、参数快照（强类型）、设备运行时与记录。
/// </summary>
public sealed record ItemExecutionContext(
    UserContext Actor,
    TestRecord Record,
    RecipeItemContext RecipeItem,
    TestItemDefinition Definition,
    IReadOnlyDictionary<string, string> ParameterValues,
    DeviceMode Mode,
    Ports.IDeviceRuntime Runtime,
    EffectiveTestParameters? EffectiveParameters = null);

/// <summary>
/// 执行项定位信息：Id 在固定序列中表示试验项定义 ID。
/// </summary>
public sealed record RecipeItemContext(int Id, int SortOrder, bool IsEnabled);

public sealed record ItemExecutionOutcome(ItemResultState State, string? SummaryValue, string? ResultText);

/// <summary>
/// 试验项执行器扩展点。具体试验算法通过实现本接口注册，不允许运行时编译任意 C#。
/// </summary>
public interface ITestItemExecutor
{
    string ExecutorCode { get; }
    Task<ItemExecutionOutcome> ExecuteAsync(ItemExecutionContext context, CancellationToken ct = default);
}
