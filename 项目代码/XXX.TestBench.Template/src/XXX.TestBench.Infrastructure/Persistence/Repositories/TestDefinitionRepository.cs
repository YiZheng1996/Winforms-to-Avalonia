using System.Text.Json;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public sealed class TestDefinitionRepository : SqliteRepositoryBase, ITestDefinitionRepository
{
    public TestDefinitionRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<TestItemDefinition?> GetItemAsync(int id, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, code, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc FROM test_item_definitions WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? MapItem(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<TestItemDefinition>> ListItemsAsync(bool includeDisabled, CancellationToken ct = default)
    {
        var result = new List<TestItemDefinition>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = includeDisabled
                ? "SELECT id, code, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc FROM test_item_definitions ORDER BY sort_order"
                : "SELECT id, code, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc FROM test_item_definitions WHERE is_enabled=1 ORDER BY sort_order";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(MapItem(reader));
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<ParameterDefinition>> ListParametersAsync(int itemId, CancellationToken ct = default)
    {
        var result = new List<ParameterDefinition>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, test_item_definition_id, code, name, data_type, unit, is_required, min_value, max_value, precision, allowed_values, sort_order FROM parameter_definitions WHERE test_item_definition_id=$id ORDER BY sort_order";
            cmd.Parameters.AddWithValue("$id", itemId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(MapParameter(reader));
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<ParameterDefinition?> GetParameterAsync(int id, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, test_item_definition_id, code, name, data_type, unit, is_required, min_value, max_value, precision, allowed_values, sort_order FROM parameter_definitions WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? MapParameter(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddItemAsync(TestItemDefinition item, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO test_item_definitions (code, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc) VALUES ($code, $name, $executor, $kind, $enabled, $sort, $created)";
            cmd.Parameters.AddWithValue("$code", item.Code);
            cmd.Parameters.AddWithValue("$name", item.Name);
            cmd.Parameters.AddWithValue("$executor", item.ExecutorCode);
            cmd.Parameters.AddWithValue("$kind", item.ResultKind);
            cmd.Parameters.AddWithValue("$enabled", item.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$sort", item.SortOrder);
            cmd.Parameters.AddWithValue("$created", item.CreatedAtUtc.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
            item.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddParameterAsync(ParameterDefinition parameter, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO parameter_definitions (test_item_definition_id, code, name, data_type, unit, is_required, min_value, max_value, precision, allowed_values, sort_order) VALUES ($tid, $code, $name, $type, $unit, $required, $min, $max, $precision, $allowed, $sort)";
            cmd.Parameters.AddWithValue("$tid", parameter.TestItemDefinitionId);
            cmd.Parameters.AddWithValue("$code", parameter.Code);
            cmd.Parameters.AddWithValue("$name", parameter.Name);
            cmd.Parameters.AddWithValue("$type", (int)parameter.DataType);
            cmd.Parameters.AddWithValue("$unit", (object?)parameter.Unit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$required", parameter.IsRequired ? 1 : 0);
            cmd.Parameters.AddWithValue("$min", (object?)parameter.MinValue?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$max", (object?)parameter.MaxValue?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$precision", (object?)parameter.Precision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$allowed", parameter.AllowedValues.Count == 0 ? DBNull.Value : System.Text.Json.JsonSerializer.Serialize(parameter.AllowedValues));
            cmd.Parameters.AddWithValue("$sort", parameter.SortOrder);
            await cmd.ExecuteNonQueryAsync(ct);
            parameter.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }
    private static TestItemDefinition MapItem(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        ExecutorCode = r.GetString(3),
        ResultKind = r.GetString(4),
        IsEnabled = r.GetInt32(5) != 0,
        SortOrder = r.GetInt32(6),
        CreatedAtUtc = ParseUtc(r.GetString(7))
    };

    private static ParameterDefinition MapParameter(Microsoft.Data.Sqlite.SqliteDataReader r)
    {
        var allowed = ParseNullableString(r.GetValue(10));
        return new ParameterDefinition
        {
            Id = r.GetInt32(0),
            TestItemDefinitionId = r.GetInt32(1),
            Code = r.GetString(2),
            Name = r.GetString(3),
            DataType = (ParameterDataType)r.GetInt32(4),
            Unit = ParseNullableString(r.GetValue(5)),
            IsRequired = r.GetInt32(6) != 0,
            MinValue = r.IsDBNull(7) ? null : decimal.Parse(r.GetString(7)),
            MaxValue = r.IsDBNull(8) ? null : decimal.Parse(r.GetString(8)),
            Precision = r.IsDBNull(9) ? null : r.GetInt32(9),
            AllowedValues = allowed is null ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(allowed) ?? Array.Empty<string>(),
            SortOrder = r.GetInt32(11)
        };
    }
}
