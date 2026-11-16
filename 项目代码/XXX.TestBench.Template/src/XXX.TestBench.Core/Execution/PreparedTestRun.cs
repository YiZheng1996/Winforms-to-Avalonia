using XXX.TestBench.Core.Application;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 重新核对版本后的开始数据，不等于设备操作许可。
/// 协调器仍须在共享操作门内保存运行记录和操作证据后，才能安排执行。
/// </summary>
public sealed record PreparedTestRun(
    TestRunPreparationSession Preparation,
    UserContext Actor,
    Guid StartRequestId,
    Guid RunId,
    IReadOnlyDictionary<string, string> ResolvedSignalPointIds,
    bool IsReplay);
