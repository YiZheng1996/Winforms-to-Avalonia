using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Ports;

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
    public async Task<bool> ExistsRecordNumberAsync(string recordNumber, CancellationToken ct = default)
    {
        var value = await ScalarAsync("SELECT COUNT(1) FROM test_records WHERE record_number=@recordNumber", new { recordNumber }, ct);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// 新增试验记录并回填编号。
    /// </summary>
    public async Task AddRecordAsync(TestRecord record, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO test_records (record_number, product_model_id, product_number, batch_number, station_number, remark, parameter_snapshot, sequence_snapshot, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc)
            VALUES (@recordNumber, @productModelId, @productNumber, @batchNumber, @stationNumber, @remark, @parameterSnapshot, @sequenceSnapshot, @deviceMode, @operatorUserId, @state, @conclusion, @startedAtUtc, @finishedAtUtc)
            """, new
        {
            recordNumber = record.RecordNumber,
            productModelId = record.ProductModelId,
            productNumber = DbValue(record.ProductIdentity.ProductNumber),
            batchNumber = DbValue(record.ProductIdentity.BatchNumber),
            stationNumber = DbValue(record.ProductIdentity.StationNumber),
            remark = DbValue(record.ProductIdentity.Remark),
            parameterSnapshot = DbValue(record.ParameterSnapshot),
            sequenceSnapshot = DbValue(record.SequenceSnapshot),
            deviceMode = (int)record.DeviceMode,
            operatorUserId = record.OperatorUserId,
            state = (int)record.State,
            conclusion = DbValue(record.Conclusion),
            startedAtUtc = record.StartedAtUtc.ToString("O"),
            finishedAtUtc = DbValue(record.FinishedAtUtc?.ToString("O"))
        }, ct);
        record.Id = (int)id;
    }

    /// <summary>
    /// 按编号读取试验记录。
    /// </summary>
    public async Task<TestRecord?> GetRecordAsync(int recordId, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<TestRecordRow>(RecordSelect + " WHERE id=@id", new { id = recordId }, ct);
        return row is null ? null : MapRecord(row);
    }

    /// <summary>
    /// 读取当前正在执行的试验记录。
    /// </summary>
    public async Task<TestRecord?> GetActiveRunningRecordAsync(CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<TestRecordRow>(RecordSelect + " WHERE state=0 ORDER BY started_at_utc DESC, id DESC LIMIT 1", null, ct);
        return row is null ? null : MapRecord(row);
    }

    /// <summary>
    /// 按状态读取试验记录列表。
    /// </summary>
    public async Task<IReadOnlyList<TestRecord>> ListRecordsAsync(int? state, CancellationToken ct = default)
    {
        var filter = state is null ? string.Empty : " WHERE state=@state";
        var rows = await QueryAsync<TestRecordRow>(RecordSelect + filter + " ORDER BY started_at_utc DESC, id DESC", new { state }, ct);
        return rows.Select(MapRecord).ToList();
    }

    /// <summary>
    /// 更新试验记录状态与结论。
    /// </summary>
    public Task UpdateRecordAsync(TestRecord record, CancellationToken ct = default) => ExecuteAsync("""
        UPDATE test_records SET state=@state, conclusion=@conclusion, finished_at_utc=@finishedAtUtc WHERE id=@id
        """, new
    {
        state = (int)record.State,
        conclusion = DbValue(record.Conclusion),
        finishedAtUtc = DbValue(record.FinishedAtUtc?.ToString("O")),
        id = record.Id
    }, ct);

    /// <summary>
    /// 新增项点结果并回填编号。
    /// </summary>
    public async Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO test_item_results (record_id, test_item_point_id, state, summary_value, result_text, started_at_utc, finished_at_utc)
            VALUES (@recordId, @testItemPointId, @state, @summaryValue, @resultText, @startedAtUtc, @finishedAtUtc)
            """, new
        {
            recordId = result.RecordId,
            testItemPointId = result.TestItemPointId,
            state = (int)result.State,
            summaryValue = DbValue(result.SummaryValue),
            resultText = DbValue(result.ResultText),
            startedAtUtc = DbValue(result.StartedAtUtc?.ToString("O")),
            finishedAtUtc = DbValue(result.FinishedAtUtc?.ToString("O"))
        }, ct);
        result.Id = (int)id;
    }

    /// <summary>
    /// 读取某记录的项点结果列表。
    /// </summary>
    public async Task<IReadOnlyList<TestItemResult>> ListItemResultsAsync(int recordId, CancellationToken ct = default)
    {
        var rows = await QueryAsync<TestItemResultRow>(ItemResultSelect + " WHERE record_id=@recordId", new { recordId }, ct);
        return rows.Select(MapItemResult).ToList();
    }

    /// <summary>
    /// 更新项点结果。
    /// </summary>
    public Task UpdateItemResultAsync(TestItemResult result, CancellationToken ct = default) => ExecuteAsync("""
        UPDATE test_item_results SET state=@state, summary_value=@summaryValue, result_text=@resultText, started_at_utc=@startedAtUtc, finished_at_utc=@finishedAtUtc WHERE id=@id
        """, new
    {
        state = (int)result.State,
        summaryValue = DbValue(result.SummaryValue),
        resultText = DbValue(result.ResultText),
        startedAtUtc = DbValue(result.StartedAtUtc?.ToString("O")),
        finishedAtUtc = DbValue(result.FinishedAtUtc?.ToString("O")),
        id = result.Id
    }, ct);

    /// <summary>
    /// 把查询结果转换为试验记录对象。
    /// </summary>
    private static TestRecord MapRecord(TestRecordRow row) => new()
    {
        Id = row.Id,
        RecordNumber = row.RecordNumber,
        ProductModelId = row.ProductModelId,
        ProductIdentity = new ProductIdentity(row.ProductNumber, row.BatchNumber, row.StationNumber, row.Remark),
        ParameterSnapshot = row.ParameterSnapshot,
        SequenceSnapshot = row.SequenceSnapshot,
        DeviceMode = (DeviceMode)row.DeviceMode,
        OperatorUserId = row.OperatorUserId,
        State = (RecordState)row.State,
        Conclusion = row.Conclusion,
        StartedAtUtc = ParseUtc(row.StartedAtUtc),
        FinishedAtUtc = ParseNullableUtc(row.FinishedAtUtc)
    };

    /// <summary>
    /// 把查询结果转换为项点结果对象。
    /// </summary>
    private static TestItemResult MapItemResult(TestItemResultRow row) => new()
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

    /// <summary>
    /// 试验记录常用查询字段。
    /// </summary>
    private const string RecordSelect = "SELECT id AS Id, record_number AS RecordNumber, product_model_id AS ProductModelId, product_number AS ProductNumber, batch_number AS BatchNumber, station_number AS StationNumber, remark AS Remark, parameter_snapshot AS ParameterSnapshot, sequence_snapshot AS SequenceSnapshot, device_mode AS DeviceMode, operator_user_id AS OperatorUserId, state AS State, conclusion AS Conclusion, started_at_utc AS StartedAtUtc, finished_at_utc AS FinishedAtUtc FROM test_records";
    /// <summary>
    /// 项点结果常用查询字段。
    /// </summary>
    private const string ItemResultSelect = "SELECT id AS Id, record_id AS RecordId, test_item_point_id AS TestItemPointId, state AS State, summary_value AS SummaryValue, result_text AS ResultText, started_at_utc AS StartedAtUtc, finished_at_utc AS FinishedAtUtc FROM test_item_results";

    private sealed class TestRecordRow
    {
        public int Id { get; set; }
        public string RecordNumber { get; set; } = string.Empty;
        public int ProductModelId { get; set; }
        public string? ProductNumber { get; set; }
        public string? BatchNumber { get; set; }
        public string? StationNumber { get; set; }
        public string? Remark { get; set; }
        public string? ParameterSnapshot { get; set; }
        public string? SequenceSnapshot { get; set; }
        public int DeviceMode { get; set; }
        public int OperatorUserId { get; set; }
        public int State { get; set; }
        public string? Conclusion { get; set; }
        public string StartedAtUtc { get; set; } = string.Empty;
        public string? FinishedAtUtc { get; set; }
    }

    private sealed class TestItemResultRow
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public int TestItemPointId { get; set; }
        public int State { get; set; }
        public string? SummaryValue { get; set; }
        public string? ResultText { get; set; }
        public string? StartedAtUtc { get; set; }
        public string? FinishedAtUtc { get; set; }
    }
}
