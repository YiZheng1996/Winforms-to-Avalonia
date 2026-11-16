using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Domain.TestParameters;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 项点执行上下文：记录内固化的序列项点、参数快照（强类型）、受控设备访问与记录。
/// </summary>
public sealed record ItemExecutionContext(
    UserContext Actor,
    TestRecord Record,
    SequenceItemContext Sequence,
    SequenceItem Point,
    DeviceMode Mode,
    IBenchDeviceAccess DeviceAccess,
    EffectiveTestParameters EffectiveParameters);

/// <summary>
/// 执行项定位信息：Id 表示记录固化序列中的试验项点 ID。
/// </summary>
public sealed record SequenceItemContext(int Id, int SortOrder, bool IsEnabled);

/// <summary>
/// 项点执行结果。
/// </summary>
public sealed record ItemExecutionOutcome(
    ItemResultState State,
    string? SummaryValue,
    string? ResultText,
    IReadOnlyList<MeasuredValue>? Measurements = null);

/// <summary>
/// 执行器允许输出的结构化测量合同。
/// </summary>
public sealed record ItemOutputDefinition(
    string Key,
    MeasuredValueKind Kind,
    string? Unit,
    bool Required);

/// <summary>
/// 试验项点执行器扩展点。具体试验算法通过实现本接口注册（关联逻辑类），不允许运行时编译任意 C#。
/// </summary>
public interface ITestItemExecutor
{
    /// <summary>
    /// 执行器代码。
    /// </summary>
    string ExecutorCode { get; }

    /// <summary>
    /// 输出合同版本；资源快照固定实际运行的版本。
    /// </summary>
    int OutputContractVersion => 1;

    /// <summary>
    /// 仅由完成项目台架适配并审核过的执行器显式启用；默认不能用于硬件或混合模式。
    /// </summary>
    bool IsHardwareValidated => false;

    /// <summary>
    /// 执行器允许输出的结构化测量；空集合表示当前只提供列表摘要。
    /// </summary>
    IReadOnlyList<ItemOutputDefinition> Outputs => Array.Empty<ItemOutputDefinition>();

    /// <summary>
    /// 执行器声明的必需业务信号。空集合表示该执行器不依赖设备点位。
    /// </summary>
    IReadOnlyList<RequiredSignal> RequiredSignals => Array.Empty<RequiredSignal>();

    /// <summary>
    /// 仅用于显示的可选信号，不参与合格判定。
    /// </summary>
    IReadOnlyList<RequiredSignal> OptionalSignals => Array.Empty<RequiredSignal>();
    /// <summary>
    /// 执行指定项点并返回结果。
    /// </summary>
    Task<ItemExecutionOutcome> ExecuteAsync(ItemExecutionContext context, CancellationToken ct = default);
}
