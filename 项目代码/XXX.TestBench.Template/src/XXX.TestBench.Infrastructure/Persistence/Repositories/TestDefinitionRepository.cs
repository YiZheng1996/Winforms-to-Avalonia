using System.Text.Json;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public sealed class TestDefinitionRepository : SqliteRepositoryBase, ITestDefinitionRepository
{
    public TestDefinitionRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<TestItemDefinition?> GetItemAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<TestItemRow>(ItemSelect + " WHERE id=@id", new { id }, ct);
        return row is null ? null : MapItem(row);
    }

    public async Task<IReadOnlyList<TestItemDefinition>> ListItemsAsync(bool includeDisabled, CancellationToken ct = default)
    {
        var filter = includeDisabled ? string.Empty : " WHERE is_enabled=1";
        var rows = await QueryAsync<TestItemRow>(ItemSelect + filter + " ORDER BY sort_order", null, ct);
        return rows.Select(MapItem).ToList();
    }

    public async Task<IReadOnlyList<ParameterDefinition>> ListParametersAsync(int itemId, CancellationToken ct = default)
    {
        var rows = await QueryAsync<ParameterRow>(ParameterSelect + " WHERE test_item_definition_id=@itemId ORDER BY sort_order", new { itemId }, ct);
        return rows.Select(MapParameter).ToList();
    }

    public async Task<ParameterDefinition?> GetParameterAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ParameterRow>(ParameterSelect + " WHERE id=@id", new { id }, ct);
        return row is null ? null : MapParameter(row);
    }

    public async Task AddItemAsync(TestItemDefinition item, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync(
            "INSERT INTO test_item_definitions (code, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc) VALUES (@code, @name, @executorCode, @resultKind, @enabled, @sortOrder, @createdAtUtc)",
            new
            {
                code = item.Code,
                name = item.Name,
                executorCode = item.ExecutorCode,
                resultKind = item.ResultKind,
                enabled = item.IsEnabled ? 1 : 0,
                sortOrder = item.SortOrder,
                createdAtUtc = item.CreatedAtUtc.ToString("O")
            }, ct);
        item.Id = (int)id;
    }

    public async Task AddParameterAsync(ParameterDefinition parameter, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO parameter_definitions (test_item_definition_id, code, name, data_type, unit, is_required, min_value, max_value, precision, allowed_values, sort_order)
            VALUES (@testItemDefinitionId, @code, @name, @dataType, @unit, @required, @minValue, @maxValue, @precision, @allowedValues, @sortOrder)
            """, new
        {
            testItemDefinitionId = parameter.TestItemDefinitionId,
            code = parameter.Code,
            name = parameter.Name,
            dataType = (int)parameter.DataType,
            unit = DbValue(parameter.Unit),
            required = parameter.IsRequired ? 1 : 0,
            minValue = DbValue(parameter.MinValue?.ToString()),
            maxValue = DbValue(parameter.MaxValue?.ToString()),
            precision = DbValue(parameter.Precision),
            allowedValues = parameter.AllowedValues.Count == 0 ? (object)DBNull.Value : JsonSerializer.Serialize(parameter.AllowedValues),
            sortOrder = parameter.SortOrder
        }, ct);
        parameter.Id = (int)id;
    }

    public Task UpdateItemAsync(TestItemDefinition item, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE test_item_definitions SET name=@name, executor_code=@executorCode, result_kind=@resultKind, is_enabled=@enabled, sort_order=@sortOrder WHERE id=@id",
        new { name = item.Name, executorCode = item.ExecutorCode, resultKind = item.ResultKind, enabled = item.IsEnabled ? 1 : 0, sortOrder = item.SortOrder, id = item.Id }, ct);

    public Task UpdateParameterAsync(ParameterDefinition parameter, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE parameter_definitions SET name=@name, unit=@unit, is_required=@required, min_value=@minValue, max_value=@maxValue, precision=@precision, allowed_values=@allowedValues, sort_order=@sortOrder WHERE id=@id",
        new
        {
            name = parameter.Name,
            unit = DbValue(parameter.Unit),
            required = parameter.IsRequired ? 1 : 0,
            minValue = DbValue(parameter.MinValue?.ToString()),
            maxValue = DbValue(parameter.MaxValue?.ToString()),
            precision = DbValue(parameter.Precision),
            allowedValues = parameter.AllowedValues.Count == 0 ? (object)DBNull.Value : JsonSerializer.Serialize(parameter.AllowedValues),
            sortOrder = parameter.SortOrder,
            id = parameter.Id
        }, ct);

    private static TestItemDefinition MapItem(TestItemRow row) => new()
    {
        Id = row.Id,
        Code = row.Code,
        Name = row.Name,
        ExecutorCode = row.ExecutorCode,
        ResultKind = row.ResultKind,
        IsEnabled = row.IsEnabled != 0,
        SortOrder = row.SortOrder,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
    };

    private static ParameterDefinition MapParameter(ParameterRow row)
    {
        var allowed = ParseNullableString(row.AllowedValues);
        return new ParameterDefinition
        {
            Id = row.Id,
            TestItemDefinitionId = row.TestItemDefinitionId,
            Code = row.Code,
            Name = row.Name,
            DataType = (ParameterDataType)row.DataType,
            Unit = row.Unit,
            IsRequired = row.IsRequired != 0,
            MinValue = row.MinValue is null ? null : decimal.Parse(row.MinValue),
            MaxValue = row.MaxValue is null ? null : decimal.Parse(row.MaxValue),
            Precision = row.Precision,
            AllowedValues = allowed is null ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(allowed) ?? Array.Empty<string>(),
            SortOrder = row.SortOrder
        };
    }

    private const string ItemSelect = "SELECT id AS Id, code AS Code, name AS Name, executor_code AS ExecutorCode, result_kind AS ResultKind, is_enabled AS IsEnabled, sort_order AS SortOrder, created_at_utc AS CreatedAtUtc FROM test_item_definitions";
    private const string ParameterSelect = "SELECT id AS Id, test_item_definition_id AS TestItemDefinitionId, code AS Code, name AS Name, data_type AS DataType, unit AS Unit, is_required AS IsRequired, min_value AS MinValue, max_value AS MaxValue, precision AS Precision, allowed_values AS AllowedValues, sort_order AS SortOrder FROM parameter_definitions";

    private sealed class TestItemRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ExecutorCode { get; set; } = string.Empty;
        public string ResultKind { get; set; } = string.Empty;
        public int IsEnabled { get; set; }
        public int SortOrder { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class ParameterRow
    {
        public int Id { get; set; }
        public int TestItemDefinitionId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int DataType { get; set; }
        public string? Unit { get; set; }
        public int IsRequired { get; set; }
        public string? MinValue { get; set; }
        public string? MaxValue { get; set; }
        public int? Precision { get; set; }
        public string? AllowedValues { get; set; }
        public int SortOrder { get; set; }
    }
}
