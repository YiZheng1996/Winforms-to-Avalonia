using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 任务/记录/项点结果的 SQLite 仓储；任务不关联配方，记录保存直编参数快照。
/// </summary>
public sealed class TaskRepository : SqliteRepositoryBase, ITaskRepository
{
    public TaskRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<TestTask?> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<TestTaskRow>(TaskSelect + " WHERE id=@id", new { id }, ct);
        return row is null ? null : Map(row);
    }

    public async Task<bool> ExistsTaskNumberAsync(string taskNumber, CancellationToken ct = default)
    {
        var value = await ScalarAsync("SELECT COUNT(1) FROM test_tasks WHERE task_number=@taskNumber", new { taskNumber }, ct);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    public async Task<TestTask?> GetActiveRunningAsync(CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<TestTaskRow>(TaskSelect + " WHERE state=2 LIMIT 1", null, ct);
        return row is null ? null : Map(row);
    }

    public async Task AddAsync(TestTask task, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO test_tasks (task_number, product_model_id, product_number, batch_number, station_number, remark, state, created_by_user_id, created_at_utc, started_at_utc, finished_at_utc)
            VALUES (@taskNumber, @productModelId, @productNumber, @batchNumber, @stationNumber, @remark, @state, @createdByUserId, @createdAtUtc, @startedAtUtc, @finishedAtUtc)
            """, new
        {
            taskNumber = task.TaskNumber,
            productModelId = task.ProductModelId,
            productNumber = DbValue(task.ProductIdentity.ProductNumber),
            batchNumber = DbValue(task.ProductIdentity.BatchNumber),
            stationNumber = DbValue(task.ProductIdentity.StationNumber),
            remark = DbValue(task.ProductIdentity.Remark),
            state = (int)task.State,
            createdByUserId = task.CreatedByUserId,
            createdAtUtc = task.CreatedAtUtc.ToString("O"),
            startedAtUtc = DbValue(task.StartedAtUtc?.ToString("O")),
            finishedAtUtc = DbValue(task.FinishedAtUtc?.ToString("O"))
        }, ct);
        task.Id = (int)id;
    }

    public Task UpdateAsync(TestTask task, CancellationToken ct = default) => ExecuteAsync("""
        UPDATE test_tasks SET product_number=@productNumber, batch_number=@batchNumber, station_number=@stationNumber, remark=@remark,
        state=@state, started_at_utc=@startedAtUtc, finished_at_utc=@finishedAtUtc WHERE id=@id
        """, new
    {
        productNumber = DbValue(task.ProductIdentity.ProductNumber),
        batchNumber = DbValue(task.ProductIdentity.BatchNumber),
        stationNumber = DbValue(task.ProductIdentity.StationNumber),
        remark = DbValue(task.ProductIdentity.Remark),
        state = (int)task.State,
        startedAtUtc = DbValue(task.StartedAtUtc?.ToString("O")),
        finishedAtUtc = DbValue(task.FinishedAtUtc?.ToString("O")),
        id = task.Id
    }, ct);

    public async Task AddRecordAsync(TestRecord record, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO test_records (task_id, parameter_snapshot, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc)
            VALUES (@taskId, @parameterSnapshot, @deviceMode, @operatorUserId, @state, @conclusion, @startedAtUtc, @finishedAtUtc)
            """, new
        {
            taskId = record.TaskId,
            parameterSnapshot = DbValue(record.ParameterSnapshot),
            deviceMode = (int)record.DeviceMode,
            operatorUserId = record.OperatorUserId,
            state = (int)record.State,
            conclusion = DbValue(record.Conclusion),
            startedAtUtc = record.StartedAtUtc.ToString("O"),
            finishedAtUtc = DbValue(record.FinishedAtUtc?.ToString("O"))
        }, ct);
        record.Id = (int)id;
    }

    public async Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO test_item_results (record_id, recipe_item_id, test_item_definition_id, state, summary_value, result_text, started_at_utc, finished_at_utc)
            VALUES (@recordId, @recipeItemId, @testItemDefinitionId, @state, @summaryValue, @resultText, @startedAtUtc, @finishedAtUtc)
            """, new
        {
            recordId = result.RecordId,
            recipeItemId = DbValue(result.RecipeItemId),
            testItemDefinitionId = result.TestItemDefinitionId,
            state = (int)result.State,
            summaryValue = DbValue(result.SummaryValue),
            resultText = DbValue(result.ResultText),
            startedAtUtc = DbValue(result.StartedAtUtc?.ToString("O")),
            finishedAtUtc = DbValue(result.FinishedAtUtc?.ToString("O"))
        }, ct);
        result.Id = (int)id;
    }

    public async Task<TestRecord?> GetRecordAsync(int recordId, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<TestRecordRow>(RecordSelect + " WHERE id=@recordId", new { recordId }, ct);
        return row is null ? null : MapRecord(row);
    }

    public Task UpdateRecordAsync(TestRecord record, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE test_records SET state=@state, conclusion=@conclusion, finished_at_utc=@finishedAtUtc WHERE id=@id",
        new { state = (int)record.State, conclusion = DbValue(record.Conclusion), finishedAtUtc = DbValue(record.FinishedAtUtc?.ToString("O")), id = record.Id }, ct);

    public async Task<IReadOnlyList<TestItemResult>> ListItemResultsAsync(int recordId, CancellationToken ct = default)
    {
        var rows = await QueryAsync<TestItemResultRow>(
            ItemResultSelect + " WHERE record_id=@recordId ORDER BY id", new { recordId }, ct);
        return rows.Select(MapItemResult).ToList();
    }

    public Task UpdateItemResultAsync(TestItemResult result, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE test_item_results SET state=@state, summary_value=@summaryValue, result_text=@resultText, started_at_utc=@startedAtUtc, finished_at_utc=@finishedAtUtc WHERE id=@id",
        new
        {
            state = (int)result.State,
            summaryValue = DbValue(result.SummaryValue),
            resultText = DbValue(result.ResultText),
            startedAtUtc = DbValue(result.StartedAtUtc?.ToString("O")),
            finishedAtUtc = DbValue(result.FinishedAtUtc?.ToString("O")),
            id = result.Id
        }, ct);

    public async Task<IReadOnlyList<TestTask>> ListTasksAsync(int? state, CancellationToken ct = default)
    {
        var filter = state is null ? string.Empty : " WHERE state=@state";
        var rows = await QueryAsync<TestTaskRow>(TaskSelect + filter + " ORDER BY id DESC", new { state }, ct);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<TestRecord>> ListRecordsAsync(int? state, CancellationToken ct = default)
    {
        var filter = state is null ? string.Empty : " WHERE state=@state";
        var rows = await QueryAsync<TestRecordRow>(RecordSelect + filter + " ORDER BY id DESC", new { state }, ct);
        return rows.Select(MapRecord).ToList();
    }

    private static TestTask Map(TestTaskRow row) => new()
    {
        Id = row.Id,
        TaskNumber = row.TaskNumber,
        ProductModelId = row.ProductModelId,
        ProductIdentity = new ProductIdentity(row.ProductNumber, row.BatchNumber, row.StationNumber, row.Remark),
        State = (TaskState)row.State,
        CreatedByUserId = row.CreatedByUserId,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc),
        StartedAtUtc = ParseNullableUtc(row.StartedAtUtc),
        FinishedAtUtc = ParseNullableUtc(row.FinishedAtUtc)
    };

    private static TestRecord MapRecord(TestRecordRow row) => new()
    {
        Id = row.Id,
        TaskId = row.TaskId,
        ParameterSnapshot = row.ParameterSnapshot,
        DeviceMode = (DeviceMode)row.DeviceMode,
        OperatorUserId = row.OperatorUserId,
        State = (RecordState)row.State,
        Conclusion = row.Conclusion,
        StartedAtUtc = ParseUtc(row.StartedAtUtc),
        FinishedAtUtc = ParseNullableUtc(row.FinishedAtUtc)
    };

    private static TestItemResult MapItemResult(TestItemResultRow row) => new()
    {
        Id = row.Id,
        RecordId = row.RecordId,
        RecipeItemId = row.RecipeItemId,
        TestItemDefinitionId = row.TestItemDefinitionId,
        State = (ItemResultState)row.State,
        SummaryValue = row.SummaryValue,
        ResultText = row.ResultText,
        StartedAtUtc = ParseNullableUtc(row.StartedAtUtc),
        FinishedAtUtc = ParseNullableUtc(row.FinishedAtUtc)
    };

    private const string TaskSelect = "SELECT id AS Id, task_number AS TaskNumber, product_model_id AS ProductModelId, product_number AS ProductNumber, batch_number AS BatchNumber, station_number AS StationNumber, remark AS Remark, state AS State, created_by_user_id AS CreatedByUserId, created_at_utc AS CreatedAtUtc, started_at_utc AS StartedAtUtc, finished_at_utc AS FinishedAtUtc FROM test_tasks";
    private const string RecordSelect = "SELECT id AS Id, task_id AS TaskId, parameter_snapshot AS ParameterSnapshot, device_mode AS DeviceMode, operator_user_id AS OperatorUserId, state AS State, conclusion AS Conclusion, started_at_utc AS StartedAtUtc, finished_at_utc AS FinishedAtUtc FROM test_records";
    private const string ItemResultSelect = "SELECT id AS Id, record_id AS RecordId, recipe_item_id AS RecipeItemId, test_item_definition_id AS TestItemDefinitionId, state AS State, summary_value AS SummaryValue, result_text AS ResultText, started_at_utc AS StartedAtUtc, finished_at_utc AS FinishedAtUtc FROM test_item_results";

    private sealed class TestTaskRow
    {
        public int Id { get; set; }
        public string TaskNumber { get; set; } = string.Empty;
        public int ProductModelId { get; set; }
        public string? ProductNumber { get; set; }
        public string? BatchNumber { get; set; }
        public string? StationNumber { get; set; }
        public string? Remark { get; set; }
        public int State { get; set; }
        public int CreatedByUserId { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string? StartedAtUtc { get; set; }
        public string? FinishedAtUtc { get; set; }
    }

    private sealed class TestRecordRow
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public string? ParameterSnapshot { get; set; }
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
        public int? RecipeItemId { get; set; }
        public int TestItemDefinitionId { get; set; }
        public int State { get; set; }
        public string? SummaryValue { get; set; }
        public string? ResultText { get; set; }
        public string? StartedAtUtc { get; set; }
        public string? FinishedAtUtc { get; set; }
    }
}
