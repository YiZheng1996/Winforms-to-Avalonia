using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 试验记录与项点结果的数据库实现；记录直接关联产品型号并固化快照，不依赖任务。
/// </summary>
public sealed class RecordRepository : SqliteRepositoryBase, IRecordRepository
{
    /// <summary>
    /// 创建记录仓库。
    /// </summary>
    public RecordRepository(ISqliteConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 判断记录流水号是否已存在。
    /// </summary>
    public Task<bool> ExistsRecordNumberAsync(string recordNumber, CancellationToken ct = default)
        => RunDbAsync(() => Select<SqliteTestRecord>()
            .Where(x => x.RecordNumber == recordNumber)
            .Any(), ct);

    /// <summary>
    /// 新增试验记录并回填编号。
    /// </summary>
    public async Task AddRecordAsync(TestRecord record, CancellationToken ct = default)
    {
        // 正常写入只能使用最新合同，历史导入由离线工具单独完成。
        if (record.ResultContractVersion != 2 || record.EvaluationState == EvaluationState.Legacy)
            throw new DomainException("新建试验记录仅接受结果合同版本 2，禁止写入旧版兼容记录");
        if (!IsCurrentId(record.RunId) || !IsCurrentId(record.OperationId) || !IsCurrentId(record.StartRequestId)
            || string.IsNullOrWhiteSpace(record.BenchId) || string.IsNullOrWhiteSpace(record.NodeId)
            || record.Scope is null || !Enum.IsDefined(record.Scope.Value))
            throw new DomainException("新建试验记录缺少有效运行、操作、请求、台架、节点身份或执行范围");
        var id = await InsertIdentityAsync(new SqliteTestRecord
        {
            RecordNumber = record.RecordNumber,
            RunId = record.RunId ?? throw new DomainException("v9试验记录必须有稳定 RunId"),
            OperationId = record.OperationId,
            StartRequestId = record.StartRequestId,
            ProductModelId = record.ProductModelId,
            ProductNumber = record.ProductIdentity.ProductNumber,
            VehicleNumber = record.ProductIdentity.VehicleNumber,
            BatchNumber = record.ProductIdentity.BatchNumber,
            StationNumber = record.ProductIdentity.StationNumber,
            Remark = record.ProductIdentity.Remark,
            ParameterSnapshot = record.ParameterSnapshot,
            SequenceSnapshot = record.SequenceSnapshot,
            DeviceMode = (int)record.DeviceMode,
            DeviceConfigurationRevision = record.DeviceConfigurationRevision,
            SignalBindingsSnapshot = record.SignalBindingsSnapshot,
            OperatorUserId = record.OperatorUserId,
            State = (int)record.State,
            Conclusion = record.Conclusion,
            BenchId = record.BenchId,
            NodeId = record.NodeId,
            ResultContractVersion = record.ResultContractVersion,
            RunScope = record.Scope is { } scope ? (int)scope : null,
            PreviousRunId = record.PreviousRunId,
            EvaluationState = record.EvaluationState is { } evaluationState ? (int)evaluationState : null,
            EvaluationText = record.EvaluationText,
            ConclusionSource = record.ConclusionSource,
            ExecutionError = record.ExecutionError,
            ModelNameSnapshot = record.ModelNameSnapshot,
            OperatorNameSnapshot = record.OperatorNameSnapshot,
            ResourceSnapshotJson = record.ResourceSnapshotJson,
            PrecheckSnapshotJson = record.PrecheckSnapshotJson,
            ReportInputJson = record.ReportInputJson,
            InputHash = record.InputHash,
            ExecutionEvidenceKey = record.ExecutionEvidenceKey,
            CurrentReportRevisionId = record.CurrentReportRevisionId,
            RowVersion = record.RowVersion,
            StartedAtUtc = record.StartedAtUtc.ToString("O"),
            FinishedAtUtc = record.FinishedAtUtc?.ToString("O")
        }, ct);
        record.Id = checked((int)id);
    }

    private static bool IsCurrentId(string? value)
        => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty
           && string.Equals(value, id.ToString("D"), StringComparison.Ordinal);

    /// <summary>
    /// 按编号读取试验记录。
    /// </summary>
    public async Task<TestRecord?> GetRecordAsync(int recordId, CancellationToken ct = default)
    {
        var row = await RunDbAsync(() => Select<SqliteTestRecord>()
            .Where(x => x.Id == recordId)
            .ToOne(), ct);
        return row is null ? null : MapRecord(row);
    }

    /// <summary>
    /// 按试验台身份读取当前正在执行的试验记录。
    /// </summary>
    public async Task<TestRecord?> GetActiveRunningRecordAsync(string benchId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchId);
        var row = await RunDbAsync(() => Select<SqliteTestRecord>()
            .Where(x => x.BenchId == benchId && x.State == (int)RecordState.Running)
            .OrderByDescending(x => x.StartedAtUtc)
            .OrderByDescending(x => x.Id)
            .Limit(1)
            .ToOne(), ct);
        return row is null ? null : MapRecord(row);
    }

    /// <summary>
    /// 按状态读取试验记录列表。
    /// </summary>
    public async Task<IReadOnlyList<TestRecord>> ListRecordsAsync(int? state, CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() =>
        {
            var query = Select<SqliteTestRecord>();
            if (state is int stateValue)
                query = query.Where(x => x.State == stateValue);
            return query
                .OrderByDescending(x => x.StartedAtUtc)
                .OrderByDescending(x => x.Id)
                .ToList();
        }, ct);
        return rows.Select(MapRecord).ToList();
    }

    /// <summary>
    /// 更新试验记录状态与结论。
    /// </summary>
    public Task UpdateRecordAsync(TestRecord record, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            if (record.ResultContractVersion != 2)
                throw new DomainException("历史合同记录只允许查询，不能通过当前仓储推进状态");
            var expectedVersion = record.RowVersion;
            var affected = Update<SqliteTestRecord>()
                .Where(x => x.Id == record.Id && x.ResultContractVersion == 2
                    && x.State == (int)RecordState.Running && x.RowVersion == expectedVersion)
                .Set(x => x.State, (int)record.State)
                .Set(x => x.Conclusion, record.Conclusion)
                .Set(x => x.EvaluationState, record.EvaluationState is { } state ? (int?)state : null)
                .Set(x => x.EvaluationText, record.EvaluationText)
                .Set(x => x.ConclusionSource, record.ConclusionSource)
                .Set(x => x.ExecutionError, record.ExecutionError)
                .Set(x => x.ReportInputJson, record.ReportInputJson)
                .Set(x => x.InputHash, record.InputHash)
                .Set(x => x.ExecutionEvidenceKey, record.ExecutionEvidenceKey)
                .Set(x => x.CurrentReportRevisionId, record.CurrentReportRevisionId)
                .Set(x => x.FinishedAtUtc, record.FinishedAtUtc?.ToString("O"))
                .Set(x => x.RowVersion, checked(expectedVersion + 1))
                .ExecuteAffrows();
            if (affected != 1)
                throw new DomainException("试验记录已被其他操作更新，拒绝覆盖终态。");
            record.RowVersion = checked(expectedVersion + 1);
        }, ct);

    /// <summary>
    /// 新增项点结果并回填编号。
    /// </summary>
    public async Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default)
    {
        var id = await InsertIdentityAsync(new SqliteTestItemResult
        {
            RecordId = result.RecordId,
            TestItemPointId = result.TestItemPointId,
            State = (int)result.State,
            SummaryValue = result.SummaryValue,
            ResultText = result.ResultText,
            MeasurementsJson = result.Measurements.Count == 0
                ? null
                : MeasuredValuesSnapshotSerializer.Serialize(result.Measurements).JsonText,
            ExecutionSlotKey = result.ExecutionSlotKey,
            RowVersion = result.RowVersion,
            StartedAtUtc = result.StartedAtUtc?.ToString("O"),
            FinishedAtUtc = result.FinishedAtUtc?.ToString("O")
        }, ct);
        result.Id = checked((int)id);
    }

    /// <summary>
    /// 读取某记录的项点结果列表。
    /// </summary>
    public async Task<IReadOnlyList<TestItemResult>> ListItemResultsAsync(int recordId, CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() => Select<SqliteTestItemResult>()
            .Where(x => x.RecordId == recordId)
            .OrderBy(x => x.Id)
            .ToList(), ct);
        return rows.Select(MapItemResult).ToList();
    }

    /// <summary>
    /// 更新项点结果。
    /// </summary>
    public Task UpdateItemResultAsync(TestItemResult result, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            var expectedVersion = result.RowVersion;
            var affected = Update<SqliteTestItemResult>()
                .Where(x => x.Id == result.Id
                    && x.RecordId == result.RecordId
                    && x.State == (int)result.ExpectedPersistedState
                    && x.RowVersion == expectedVersion)
                .Set(x => x.State, (int)result.State)
                .Set(x => x.SummaryValue, result.SummaryValue)
                .Set(x => x.ResultText, result.ResultText)
                .Set(x => x.MeasurementsJson, result.Measurements.Count == 0
                    ? null
                    : MeasuredValuesSnapshotSerializer.Serialize(result.Measurements).JsonText)
                .Set(x => x.StartedAtUtc, result.StartedAtUtc?.ToString("O"))
                .Set(x => x.FinishedAtUtc, result.FinishedAtUtc?.ToString("O"))
                .Set(x => x.RowVersion, checked(expectedVersion + 1))
                .ExecuteAffrows();
            if (affected != 1)
                throw new DomainException("项点结果状态或版本已变化，拒绝重复执行或覆盖迟到结果。");
            result.ConfirmPersisted(checked(expectedVersion + 1));
        }, ct);

    /// <summary>
    /// 把查询结果转换为试验记录对象。
    /// </summary>
    private static TestRecord MapRecord(SqliteTestRecord row) => new()
    {
        Id = row.Id,
        RecordNumber = row.RecordNumber,
        RunId = row.RunId,
        OperationId = row.OperationId,
        StartRequestId = row.StartRequestId,
        BenchId = row.BenchId,
        NodeId = row.NodeId,
        ProductModelId = row.ProductModelId,
        ProductIdentity = new ProductIdentity(row.ProductNumber, row.BatchNumber, row.StationNumber, row.Remark, row.VehicleNumber),
        ModelNameSnapshot = row.ModelNameSnapshot,
        OperatorNameSnapshot = row.OperatorNameSnapshot,
        ResultContractVersion = row.ResultContractVersion,
        Scope = row.RunScope is { } scope ? (RunScope)scope : null,
        PreviousRunId = row.PreviousRunId,
        ParameterSnapshot = row.ParameterSnapshot,
        SequenceSnapshot = row.SequenceSnapshot,
        DeviceMode = (DeviceMode)row.DeviceMode,
        DeviceConfigurationRevision = row.DeviceConfigurationRevision,
        SignalBindingsSnapshot = row.SignalBindingsSnapshot,
        OperatorUserId = row.OperatorUserId,
        EvaluationState = row.EvaluationState is { } evaluationState ? (EvaluationState)evaluationState : null,
        EvaluationText = row.EvaluationText,
        ConclusionSource = row.ConclusionSource,
        ExecutionError = row.ExecutionError,
        ResourceSnapshotJson = row.ResourceSnapshotJson,
        PrecheckSnapshotJson = row.PrecheckSnapshotJson,
        ReportInputJson = row.ReportInputJson,
        InputHash = row.InputHash,
        ExecutionEvidenceKey = row.ExecutionEvidenceKey,
        CurrentReportRevisionId = row.CurrentReportRevisionId,
        RowVersion = row.RowVersion,
        State = (RecordState)row.State,
        Conclusion = row.Conclusion,
        StartedAtUtc = ParseUtc(row.StartedAtUtc),
        FinishedAtUtc = ParseNullableUtc(row.FinishedAtUtc)
    };

    /// <summary>
    /// 把查询结果转换为项点结果对象。
    /// </summary>
    private static TestItemResult MapItemResult(SqliteTestItemResult row)
    {
        var result = new TestItemResult
        {
            Id = row.Id,
            RecordId = row.RecordId,
            TestItemPointId = row.TestItemPointId,
            State = (ItemResultState)row.State,
            SummaryValue = row.SummaryValue,
            ResultText = row.ResultText,
            ExecutionSlotKey = row.ExecutionSlotKey,
            RowVersion = row.RowVersion,
            StartedAtUtc = ParseNullableUtc(row.StartedAtUtc),
            FinishedAtUtc = ParseNullableUtc(row.FinishedAtUtc)
        };
        result.RestorePersistedState((ItemResultState)row.State);
        result.RestoreMeasurements(row.MeasurementsJson);
        return result;
    }
}
