using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Domain.TestPoints;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 当前进程内的试验准备快照。到期或任一输入版本变化后必须重新准备。
/// </summary>
public sealed record TestRunPreparationSession(
    Guid PreparationSessionId,
    Guid OperationId,
    int PreparedByUserId,
    string BenchId,
    string NodeId,
    int ProductTypeId,
    string ProductTypeName,
    long ProductTypeRowVersion,
    int ProductModelId,
    string ProductModelName,
    long ProductModelRowVersion,
    long SequenceRevision,
    ProductIdentity ProductIdentity,
    RunScope Scope,
    int? SelectedPointId,
    IReadOnlyList<SequenceItem> Sequence,
    EffectiveTestParameters Parameters,
    string ParameterSnapshotJson,
    string SequenceSnapshotJson,
    string VersionVectorJson,
    string DeviceConfigurationRevision,
    string SignalBindingsSnapshotJson,
    string PreparationHash,
    DateTime PreparedAtUtc,
    DateTime ExpiresAtUtc);
