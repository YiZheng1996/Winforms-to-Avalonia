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
        var id = await InsertIdentityAsync(new SqliteTestRecord
        {
            RecordNumber = record.RecordNumber,
            ProductModelId = record.ProductModelId,
            ProductNumber = record.ProductIdentity.ProductNumber,
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
            StartedAtUtc = record.StartedAtUtc.ToString("O"),
            FinishedAtUtc = record.FinishedAtUtc?.ToString("O")
        }, ct);
        record.Id = checked((int)id);
    }

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
    /// 读取当前正在执行的试验记录。
    /// </summary>
    public async Task<TestRecord?> GetActiveRunningRecordAsync(CancellationToken ct = default)
    {
        var row = await RunDbAsync(() => Select<SqliteTestRecord>()
            .Where(x => x.State == (int)RecordState.Running)
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
            Update<SqliteTestRecord>()
                .Where(x => x.Id == record.Id)
                .Set(x => x.State, (int)record.State)
                .Set(x => x.Conclusion, record.Conclusion)
                .Set(x => x.FinishedAtUtc, record.FinishedAtUtc?.ToString("O"))
                .ExecuteAffrows();
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
            Update<SqliteTestItemResult>()
                .Where(x => x.Id == result.Id)
                .Set(x => x.State, (int)result.State)
                .Set(x => x.SummaryValue, result.SummaryValue)
                .Set(x => x.ResultText, result.ResultText)
                .Set(x => x.StartedAtUtc, result.StartedAtUtc?.ToString("O"))
                .Set(x => x.FinishedAtUtc, result.FinishedAtUtc?.ToString("O"))
                .ExecuteAffrows();
        }, ct);

    /// <summary>
    /// 把查询结果转换为试验记录对象。
    /// </summary>
    private static TestRecord MapRecord(SqliteTestRecord row) => new()
    {
        Id = row.Id,
        RecordNumber = row.RecordNumber,
        ProductModelId = row.ProductModelId,
        ProductIdentity = new ProductIdentity(row.ProductNumber, row.BatchNumber, row.StationNumber, row.Remark),
        ParameterSnapshot = row.ParameterSnapshot,
        SequenceSnapshot = row.SequenceSnapshot,
        DeviceMode = (DeviceMode)row.DeviceMode,
        DeviceConfigurationRevision = row.DeviceConfigurationRevision,
        SignalBindingsSnapshot = row.SignalBindingsSnapshot,
        OperatorUserId = row.OperatorUserId,
        State = (RecordState)row.State,
        Conclusion = row.Conclusion,
        StartedAtUtc = ParseUtc(row.StartedAtUtc),
        FinishedAtUtc = ParseNullableUtc(row.FinishedAtUtc)
    };

    /// <summary>
    /// 把查询结果转换为项点结果对象。
    /// </summary>
    private static TestItemResult MapItemResult(SqliteTestItemResult row) => new()
    {
        Id = row.Id,
        RecordId = row.RecordId,
        TestItemPointId = row.TestItemPointId,
        State = (ItemResultState)row.State,
        SummaryValue = row.SummaryValue,
        ResultText = row.ResultText,
        StartedAtUtc = ParseNullableUtc(row.StartedAtUtc),
        FinishedAtUtc = ParseNullableUtc(row.FinishedAtUtc)
    };
}
