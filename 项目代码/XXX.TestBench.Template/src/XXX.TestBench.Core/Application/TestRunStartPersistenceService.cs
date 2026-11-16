using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 正式运行记录创建的持久化结论。Unknown 时禁止发出任何设备动作并保留恢复占用。
/// </summary>
public enum RunStartPersistenceDisposition
{
    Committed = 0,
    NotCommitted = 1,
    PersistenceOutcomeUnknown = 2
}

/// <summary>
/// 开始事务结果。只有 Committed 才能把记录交给后续执行协调器。
/// </summary>
/// <param name="CreatedInThisCall">仅本次事务新建并提交时为 true；幂等重放返回 false，避免重复启动后台执行任务。</param>
public sealed record RunStartPersistenceResult(
    RunStartPersistenceDisposition Disposition,
    TestRecord? Record,
    IReadOnlyList<TestItemResult> ItemResults,
    bool CreatedInThisCall,
    Exception? Failure = null);

/// <summary>
/// 原子创建新版试验主记录及其全部 Pending 项点槽位。
/// 稳定 RunId/StartRequestId 用于并发仲裁、幂等重试和提交结果查证。
/// </summary>
public sealed class TestRunStartPersistenceService
{
    private readonly IRecordRepository _records;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public TestRunStartPersistenceService(
        IRecordRepository records,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
    }

    /// <summary>
    /// 以一个工作单元创建试验记录和冻结序列的全部结果槽位。
    /// 调用者只有收到 Committed 后才能启动执行器；此服务本身不访问设备。
    /// </summary>
    public async Task<RunStartPersistenceResult> CreateAsync(
        TestRecord runningRecord,
        IReadOnlyList<SequenceItem> frozenSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runningRecord);
        ArgumentNullException.ThrowIfNull(frozenSequence);
        ValidateRequest(runningRecord, frozenSequence);

        ExistingStart? existing;
        try
        {
            existing = await FindExistingAsync(runningRecord, frozenSequence, cancellationToken);
        }
        catch (DomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 读取不到稳定请求状态时不能假设请求尚未落库，更不能开始设备动作。
            return Unknown(ex);
        }

        if (existing is not null)
            return existing.HasAllSlots
                ? Committed(existing.Record, existing.Results, createdInThisCall: false)
                : Unknown(new InvalidDataException("已有开始请求的项点槽位与冻结序列不一致，必须进入恢复处理。"));

        IReadOnlyList<TestItemResult> newResults = Array.Empty<TestItemResult>();
        var commitAttempted = false;

        try
        {
            runningRecord.Id = 0;
            await using (var unitOfWork = _unitOfWorkFactory.Create())
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken);
                await _records.AddRecordAsync(runningRecord, cancellationToken);
                newResults = frozenSequence.Select(item => new TestItemResult
                {
                    RecordId = runningRecord.Id,
                    TestItemPointId = item.PointId,
                    ExecutionSlotKey = CreateExecutionSlotKey(runningRecord.RunId!, item.PointId),
                    RowVersion = 0
                }).ToArray();
                foreach (var result in newResults)
                {
                    await _records.AddItemResultAsync(result, cancellationToken);
                }

                commitAttempted = true;
                await unitOfWork.CommitAsync(cancellationToken);
            }

            return Committed(runningRecord, newResults, createdInThisCall: true);
        }
        catch (Exception ex)
        {
            try
            {
                // 唯一索引可能说明另一客户端已用同一标识提交；只接受内容和完整槽位都相同的请求。
                existing = await FindExistingAsync(runningRecord, frozenSequence, CancellationToken.None);
                if (existing is not null)
                {
                    if (!existing.HasAllSlots)
                        return Unknown(new InvalidDataException("开始请求已存在，但初始项点槽位不完整。", ex));
                    return Committed(existing.Record, existing.Results, createdInThisCall: false, failure: ex);
                }
            }
            catch (DomainException)
            {
                throw;
            }
            catch (Exception lookupFailure)
            {
                return Unknown(new AggregateException("开始事务失败，且无法按稳定标识查证提交结果。", ex, lookupFailure));
            }

            return commitAttempted
                ? Unknown(ex)
                : NotCommitted(ex);
        }
    }

    /// <summary>
    /// 新版记录号使用完整 RunId，避免依赖秒级时间或短流水号区分并发试验。
    /// </summary>
    public static string CreateRecordNumber(string runId)
    {
        EnsureCanonicalId(runId, nameof(runId));
        return runId;
    }

    /// <summary>
    /// 同一运行与同一冻结项点始终映射到唯一结果槽位。
    /// </summary>
    public static string CreateExecutionSlotKey(string runId, int pointId)
    {
        EnsureCanonicalId(runId, nameof(runId));
        if (pointId <= 0) throw new ArgumentOutOfRangeException(nameof(pointId));
        return string.Concat(runId, ":", pointId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task<ExistingStart?> FindExistingAsync(
        TestRecord request,
        IReadOnlyList<SequenceItem> sequence,
        CancellationToken cancellationToken)
    {
        var byRequest = await _records.GetRecordByStartRequestIdAsync(request.StartRequestId!, cancellationToken);
        var byRun = await _records.GetRecordByRunIdAsync(request.RunId!, cancellationToken);
        if (byRequest is not null && byRun is not null && byRequest.Id != byRun.Id)
            throw new DomainException("开始请求标识与运行标识分别对应不同记录，禁止继续执行。");

        var persisted = byRequest ?? byRun;
        if (persisted is null) return null;
        if (!MatchesRequest(persisted, request))
            throw new DomainException("RunId 或 StartRequestId 已用于不同的冻结试验输入，拒绝覆盖。");

        var results = await _records.ListItemResultsAsync(persisted.Id, cancellationToken);
        return new ExistingStart(persisted, results, HasAllSlots(results, request.RunId!, sequence));
    }

    private static void ValidateRequest(TestRecord record, IReadOnlyList<SequenceItem> sequence)
    {
        EnsureCanonicalId(record.RunId, nameof(record.RunId));
        EnsureCanonicalId(record.OperationId, nameof(record.OperationId));
        EnsureCanonicalId(record.StartRequestId, nameof(record.StartRequestId));

        if (record.ResultContractVersion != 2
            || record.State != RecordState.Running
            || record.FinishedAtUtc is not null
            || record.EvaluationState is not null
            || record.Conclusion is not null
            || record.EvaluationText is not null
            || record.ConclusionSource is not null
            || record.ExecutionError is not null
            || record.ReportInputJson is not null
            || record.InputHash is not null
            || record.CurrentReportRevisionId is not null
            || record.RowVersion != 0)
            throw new DomainException("开始记录必须是尚未提交的新版 Running 记录。");
        if (record.RecordNumber != CreateRecordNumber(record.RunId!))
            throw new DomainException("新版记录号必须等于完整 RunId。");
        if (record.ProductModelId <= 0 || record.OperatorUserId <= 0
            || record.ProductIdentity is null || !record.ProductIdentity.HasAnyValue)
            throw new DomainException("开始记录缺少有效型号、操作员或产品标识。");
        if (record.Scope is not { } scope || !Enum.IsDefined(scope))
            throw new DomainException("开始记录缺少有效执行范围。");
        if (!Enum.IsDefined(record.DeviceMode))
            throw new DomainException("开始记录包含未知的设备模式。");
        if (string.IsNullOrWhiteSpace(record.BenchId) || string.IsNullOrWhiteSpace(record.NodeId)
            || string.IsNullOrWhiteSpace(record.DeviceConfigurationRevision)
            || string.IsNullOrWhiteSpace(record.SignalBindingsSnapshot))
            throw new DomainException("开始记录缺少台架、本机、设备配置或信号绑定快照。");
        if (record.StartedAtUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("试验开始时间必须使用 UTC。");

        var parameters = TestParameterSnapshot.FromJson(record.ParameterSnapshot);
        if (parameters is null || parameters.ProductModelId != record.ProductModelId)
            throw new DomainException("开始记录参数快照无效或与产品型号不匹配。");
        var parameterValidation = TestParameterValidator.Validate(parameters);
        if (!parameterValidation.IsValid)
            throw new DomainException($"开始记录参数快照不符合当前参数合同：{parameterValidation.Error}");

        if (sequence.Count == 0 || sequence.Any(item => item is null))
            throw new DomainException("冻结执行序列不能为空或包含空项。");
        if (scope == RunScope.Single && sequence.Count != 1)
            throw new DomainException("单项执行范围必须且只能包含一个冻结项点。");

        string canonicalSequence;
        try
        {
            canonicalSequence = SequenceSnapshot.ToJson(sequence);
        }
        catch (ArgumentException ex)
        {
            throw new DomainException($"冻结执行序列无效：{ex.Message}");
        }
        if (!string.Equals(record.SequenceSnapshot, canonicalSequence, StringComparison.Ordinal))
            throw new DomainException("主记录序列快照与本次创建的项点槽位不一致。");
    }

    private static bool MatchesRequest(TestRecord persisted, TestRecord request)
        => persisted.ResultContractVersion == 2
           && persisted.Id > 0
           && string.Equals(persisted.RecordNumber, request.RecordNumber, StringComparison.Ordinal)
           && string.Equals(persisted.RunId, request.RunId, StringComparison.Ordinal)
           && string.Equals(persisted.StartRequestId, request.StartRequestId, StringComparison.Ordinal)
           && string.Equals(persisted.OperationId, request.OperationId, StringComparison.Ordinal)
           && string.Equals(persisted.BenchId, request.BenchId, StringComparison.Ordinal)
           && string.Equals(persisted.NodeId, request.NodeId, StringComparison.Ordinal)
           && persisted.ProductModelId == request.ProductModelId
           && persisted.ProductIdentity == request.ProductIdentity
           && persisted.ModelNameSnapshot == request.ModelNameSnapshot
           && persisted.OperatorNameSnapshot == request.OperatorNameSnapshot
           && persisted.OperatorUserId == request.OperatorUserId
           && persisted.Scope == request.Scope
           && persisted.DeviceMode == request.DeviceMode
           && persisted.DeviceConfigurationRevision == request.DeviceConfigurationRevision
           && persisted.ParameterSnapshot == request.ParameterSnapshot
           && persisted.SequenceSnapshot == request.SequenceSnapshot
           && persisted.SignalBindingsSnapshot == request.SignalBindingsSnapshot
           && persisted.ResourceSnapshotJson == request.ResourceSnapshotJson
           && persisted.PrecheckSnapshotJson == request.PrecheckSnapshotJson
           && persisted.StartedAtUtc == request.StartedAtUtc;

    private static bool HasAllSlots(
        IReadOnlyList<TestItemResult> results,
        string runId,
        IReadOnlyList<SequenceItem> sequence)
    {
        if (results.Count != sequence.Count) return false;
        var expected = sequence.ToDictionary(
            item => item.PointId,
            item => CreateExecutionSlotKey(runId, item.PointId));
        return results.Select(result => result.TestItemPointId).Distinct().Count() == results.Count
               && results.All(result => expected.TryGetValue(result.TestItemPointId, out var slot)
                   && string.Equals(result.ExecutionSlotKey, slot, StringComparison.Ordinal));
    }

    private static void EnsureCanonicalId(string? value, string parameterName)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty
            || !string.Equals(value, id.ToString("D"), StringComparison.Ordinal))
            throw new DomainException($"{parameterName} 必须为非空小写 GUID D 格式。");
    }

    private static RunStartPersistenceResult Committed(
        TestRecord record,
        IReadOnlyList<TestItemResult> results,
        bool createdInThisCall,
        Exception? failure = null)
        => new(RunStartPersistenceDisposition.Committed, record, results, createdInThisCall, failure);

    private static RunStartPersistenceResult NotCommitted(Exception failure)
        => new(RunStartPersistenceDisposition.NotCommitted, null, Array.Empty<TestItemResult>(), false, failure);

    private static RunStartPersistenceResult Unknown(Exception failure)
        => new(RunStartPersistenceDisposition.PersistenceOutcomeUnknown, null, Array.Empty<TestItemResult>(), false, failure);

    private sealed record ExistingStart(
        TestRecord Record,
        IReadOnlyList<TestItemResult> Results,
        bool HasAllSlots);
}
