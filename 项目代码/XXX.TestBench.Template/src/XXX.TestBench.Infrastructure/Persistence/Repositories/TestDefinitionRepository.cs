using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 试验项定义只读仓储。固定序列由 SqliteDatabase 种子化，运行时只查询不编辑。
/// </summary>
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
        var sql = includeDisabled ? ItemSelect : ItemSelect + " WHERE is_enabled=1";
        var rows = await QueryAsync<TestItemRow>(sql + " ORDER BY sort_order, id", null, ct);
        return rows.Select(MapItem).ToList();
    }

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

    private const string ItemSelect = "SELECT id AS Id, code AS Code, name AS Name, executor_code AS ExecutorCode, result_kind AS ResultKind, is_enabled AS IsEnabled, sort_order AS SortOrder, created_at_utc AS CreatedAtUtc FROM test_item_definitions";

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
}
