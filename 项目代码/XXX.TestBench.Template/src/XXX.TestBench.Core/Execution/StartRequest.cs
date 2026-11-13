namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 已确认准备会话的开始请求。型号、产品标识、范围和项点集合只能从会话读取。
/// 重试必须复用两个请求标识和准备摘要。
/// </summary>
public sealed record StartRequest(
    Guid StartRequestId,
    Guid OperationId,
    Guid PreparationSessionId,
    string ExpectedPreparationHash);

/// <summary>
/// 协调器创建正式记录后的稳定句柄。
/// </summary>
public sealed record RunHandle(Guid OperationId, Guid RunId, int RecordId);
