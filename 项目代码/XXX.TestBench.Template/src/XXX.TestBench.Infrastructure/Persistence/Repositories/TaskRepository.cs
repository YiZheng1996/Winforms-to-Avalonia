using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public sealed class TaskRepository : SqliteRepositoryBase, ITaskRepository
{
    public TaskRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<TestTask?> GetAsync(int id, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, task_number, product_model_id, recipe_version_id, product_number, batch_number, station_number, remark, state, created_by_user_id, created_at_utc, started_at_utc, finished_at_utc FROM test_tasks WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<bool> ExistsTaskNumberAsync(string taskNumber, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM test_tasks WHERE task_number=$tn";
            cmd.Parameters.AddWithValue("$tn", taskNumber);
            return (long)(await cmd.ExecuteScalarAsync(ct))! > 0;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<TestTask?> GetActiveRunningAsync(CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, task_number, product_model_id, recipe_version_id, product_number, batch_number, station_number, remark, state, created_by_user_id, created_at_utc, started_at_utc, finished_at_utc FROM test_tasks WHERE state=2 LIMIT 1";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddAsync(TestTask task, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO test_tasks (task_number, product_model_id, recipe_version_id, product_number, batch_number, station_number, remark, state, created_by_user_id, created_at_utc, started_at_utc, finished_at_utc)
                VALUES ($tn, $pm, $rv, $pn, $bn, $sn, $rm, $state, $creator, $created, $started, $finished)
                """;
            cmd.Parameters.AddWithValue("$tn", task.TaskNumber);
            cmd.Parameters.AddWithValue("$pm", task.ProductModelId);
            cmd.Parameters.AddWithValue("$rv", task.RecipeVersionId);
            cmd.Parameters.AddWithValue("$pn", (object?)task.ProductIdentity.ProductNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bn", (object?)task.ProductIdentity.BatchNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sn", (object?)task.ProductIdentity.StationNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rm", (object?)task.ProductIdentity.Remark ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$state", (int)task.State);
            cmd.Parameters.AddWithValue("$creator", task.CreatedByUserId);
            cmd.Parameters.AddWithValue("$created", task.CreatedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$started", (object?)task.StartedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$finished", (object?)task.FinishedAtUtc?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
            task.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateAsync(TestTask task, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE test_tasks SET product_number=$pn, batch_number=$bn, station_number=$sn, remark=$rm, state=$state, started_at_utc=$started, finished_at_utc=$finished WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", task.Id);
            cmd.Parameters.AddWithValue("$pn", (object?)task.ProductIdentity.ProductNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bn", (object?)task.ProductIdentity.BatchNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sn", (object?)task.ProductIdentity.StationNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rm", (object?)task.ProductIdentity.Remark ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$state", (int)task.State);
            cmd.Parameters.AddWithValue("$started", (object?)task.StartedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$finished", (object?)task.FinishedAtUtc?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddRecordAsync(TestRecord record, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO test_records (task_id, recipe_version_id, recipe_version_number, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc)
                VALUES ($task, $rv, $rvn, $mode, $op, $state, $conclusion, $started, $finished)
                """;
            cmd.Parameters.AddWithValue("$task", record.TaskId);
            cmd.Parameters.AddWithValue("$rv", record.RecipeVersionId);
            cmd.Parameters.AddWithValue("$rvn", record.RecipeVersionNumber);
            cmd.Parameters.AddWithValue("$mode", (int)record.DeviceMode);
            cmd.Parameters.AddWithValue("$op", record.OperatorUserId);
            cmd.Parameters.AddWithValue("$state", (int)record.State);
            cmd.Parameters.AddWithValue("$conclusion", (object?)record.Conclusion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$started", record.StartedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$finished", (object?)record.FinishedAtUtc?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
            record.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO test_item_results (record_id, recipe_item_id, test_item_definition_id, state, summary_value, result_text, started_at_utc, finished_at_utc)
                VALUES ($record, $ri, $tid, $state, $summary, $text, $started, $finished)
                """;
            cmd.Parameters.AddWithValue("$record", result.RecordId);
            cmd.Parameters.AddWithValue("$ri", result.RecipeItemId);
            cmd.Parameters.AddWithValue("$tid", result.TestItemDefinitionId);
            cmd.Parameters.AddWithValue("$state", (int)result.State);
            cmd.Parameters.AddWithValue("$summary", (object?)result.SummaryValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$text", (object?)result.ResultText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$started", (object?)result.StartedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$finished", (object?)result.FinishedAtUtc?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
            result.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<TestRecord?> GetRecordAsync(int recordId, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, task_id, recipe_version_id, recipe_version_number, device_mode, operator_user_id, state, conclusion, started_at_utc, finished_at_utc FROM test_records WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", recordId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? MapRecord(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateRecordAsync(TestRecord record, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE test_records SET state=$state, conclusion=$conclusion, finished_at_utc=$finished WHERE id=$id";
            cmd.Parameters.AddWithValue("$state", (int)record.State);
            cmd.Parameters.AddWithValue("$conclusion", (object?)record.Conclusion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$finished", (object?)record.FinishedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", record.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<TestItemResult>> ListItemResultsAsync(int recordId, CancellationToken ct = default)
    {
        var result = new List<TestItemResult>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, record_id, recipe_item_id, test_item_definition_id, state, summary_value, result_text, started_at_utc, finished_at_utc FROM test_item_results WHERE record_id=$id ORDER BY id";
            cmd.Parameters.AddWithValue("$id", recordId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(new TestItemResult
                {
                    Id = reader.GetInt32(0),
                    RecordId = reader.GetInt32(1),
                    RecipeItemId = reader.GetInt32(2),
                    TestItemDefinitionId = reader.GetInt32(3),
                    State = (ItemResultState)reader.GetInt32(4),
                    SummaryValue = ParseNullableString(reader.GetValue(5)),
                    ResultText = ParseNullableString(reader.GetValue(6)),
                    StartedAtUtc = ParseNullableUtc(reader.GetValue(7)),
                    FinishedAtUtc = ParseNullableUtc(reader.GetValue(8))
                });
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateItemResultAsync(TestItemResult result, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE test_item_results SET state=$state, summary_value=$summary, result_text=$text, started_at_utc=$started, finished_at_utc=$finished WHERE id=$id";
            cmd.Parameters.AddWithValue("$state", (int)result.State);
            cmd.Parameters.AddWithValue("$summary", (object?)result.SummaryValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$text", (object?)result.ResultText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$started", (object?)result.StartedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$finished", (object?)result.FinishedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", result.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    private static TestRecord MapRecord(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        TaskId = r.GetInt32(1),
        RecipeVersionId = r.GetInt32(2),
        RecipeVersionNumber = r.GetInt32(3),
        DeviceMode = (DeviceMode)r.GetInt32(4),
        OperatorUserId = r.GetInt32(5),
        State = (RecordState)r.GetInt32(6),
        Conclusion = ParseNullableString(r.GetValue(7)),
        StartedAtUtc = ParseUtc(r.GetString(8)),
        FinishedAtUtc = ParseNullableUtc(r.GetValue(9))
    };
    private static TestTask Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        TaskNumber = r.GetString(1),
        ProductModelId = r.GetInt32(2),
        RecipeVersionId = r.GetInt32(3),
        ProductIdentity = new ProductIdentity(ParseNullableString(r.GetValue(4)), ParseNullableString(r.GetValue(5)), ParseNullableString(r.GetValue(6)), ParseNullableString(r.GetValue(7))),
        State = (TaskState)r.GetInt32(8),
        CreatedByUserId = r.GetInt32(9),
        CreatedAtUtc = ParseUtc(r.GetString(10)),
        StartedAtUtc = ParseNullableUtc(r.GetValue(11)),
        FinishedAtUtc = ParseNullableUtc(r.GetValue(12))
    };
}
