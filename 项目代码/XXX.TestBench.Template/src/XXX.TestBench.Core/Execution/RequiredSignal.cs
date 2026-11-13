using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 执行器声明的业务信号用途。
/// </summary>
public enum SignalAccessKind
{
    Read = 1,
    Write = 2
}

/// <summary>
/// 执行器需要的固定业务信号。SignalKey 由代码声明，不能由配置任意创建执行逻辑。
/// </summary>
public sealed record RequiredSignal(
    string SignalKey,
    SignalAccessKind Access,
    DevicePointDataType ExpectedDataType,
    string Unit,
    TimeSpan MaxSampleAge);

/// <summary>
/// 运行时解析后的业务信号。
/// </summary>
public sealed record ResolvedSignal(
    string SignalKey,
    string PointId,
    DevicePoint Point,
    RequiredSignal Requirement);
