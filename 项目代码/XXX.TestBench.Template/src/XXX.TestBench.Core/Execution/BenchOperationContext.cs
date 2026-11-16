using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.TestParameters;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 由协调器创建并绑定单次操作的受控上下文。不能从 UI 或执行器自行构造。
/// </summary>
public sealed class BenchOperationContext
{
    internal BenchOperationContext(
        Guid operationId,
        Guid preparationSessionId,
        string benchId,
        string nodeId,
        Guid? runId,
        string deviceConfigurationRevision,
        IReadOnlyDictionary<string, ResolvedSignal> resolvedSignals,
        EffectiveTestParameters parameters,
        IBenchDeviceAccess deviceAccess,
        IProgress<RunProgress>? progress)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("OperationId 不能为空", nameof(operationId));
        if (preparationSessionId == Guid.Empty) throw new ArgumentException("PreparationSessionId 不能为空", nameof(preparationSessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(benchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceConfigurationRevision);
        OperationId = operationId;
        PreparationSessionId = preparationSessionId;
        BenchId = benchId;
        NodeId = nodeId;
        RunId = runId;
        DeviceConfigurationRevision = deviceConfigurationRevision;
        ResolvedSignals = resolvedSignals ?? throw new ArgumentNullException(nameof(resolvedSignals));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        DeviceAccess = deviceAccess ?? throw new ArgumentNullException(nameof(deviceAccess));
        Progress = progress;
    }

    public Guid OperationId { get; }
    public Guid PreparationSessionId { get; }
    public string BenchId { get; }
    public string NodeId { get; }
    public Guid? RunId { get; }
    public string DeviceConfigurationRevision { get; }
    public IReadOnlyDictionary<string, ResolvedSignal> ResolvedSignals { get; }
    public EffectiveTestParameters Parameters { get; }
    public IBenchDeviceAccess DeviceAccess { get; }
    public IProgress<RunProgress>? Progress { get; }
}
