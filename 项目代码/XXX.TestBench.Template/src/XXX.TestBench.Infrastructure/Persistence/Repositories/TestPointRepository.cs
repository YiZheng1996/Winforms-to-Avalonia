using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 试验项点的数据库实现；项点归属于产品类型。
/// </summary>
public sealed class TestPointRepository : SqliteRepositoryBase, ITestPointRepository
{
    /// <summary>
    /// 创建试验项点仓库。
    /// </summary>
    public TestPointRepository(ISqliteConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 按编号读取试验项点。
    /// </summary>
    public async Task<TestItemPoint?> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<TestItemPointRow>(PointSelect + " WHERE id=@id", new { id }, ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 按产品类型读取试验项点列表。
    /// </summary>
    public async Task<IReadOnlyList<TestItemPoint>> ListByTypeAsync(int productTypeId, bool includeDisabled, CancellationToken ct = default)
    {
        var sql = includeDisabled
            ? PointSelect + " WHERE product_type_id=@typeId"
            : PointSelect + " WHERE product_type_id=@typeId AND is_enabled=1";
        var rows = await QueryAsync<TestItemPointRow>(sql + " ORDER BY created_at_utc DESC, id DESC", new { typeId = productTypeId }, ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 新增试验项点并回填编号。
    /// </summary>
    public async Task AddAsync(TestItemPoint point, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO test_item_points (product_type_id, code, name, executor_code, result_kind, is_enabled, sort_order, created_at_utc, updated_at_utc)
            VALUES (@productTypeId, @code, @name, @executorCode, @resultKind, @isEnabled, @sortOrder, @createdAtUtc, @updatedAtUtc)
            """, new
        {
            productTypeId = point.ProductTypeId,
            code = $"__legacy_point_{Guid.NewGuid():N}",
            name = point.Name,
            executorCode = point.ExecutorCode,
            resultKind = point.ResultKind,
            isEnabled = point.IsEnabled ? 1 : 0,
            sortOrder = point.SortOrder,
            createdAtUtc = point.CreatedAtUtc.ToString("O"),
            updatedAtUtc = point.UpdatedAtUtc.ToString("O")
        }, ct);
        point.Id = (int)id;
    }

    /// <summary>
    /// 更新试验项点。
    /// </summary>
    public Task UpdateAsync(TestItemPoint point, CancellationToken ct = default) => ExecuteAsync("""
        UPDATE test_item_points SET name=@name, executor_code=@executorCode, result_kind=@resultKind, is_enabled=@isEnabled, sort_order=@sortOrder, updated_at_utc=@updatedAtUtc WHERE id=@id
        """, new
    {
        name = point.Name,
        executorCode = point.ExecutorCode,
        resultKind = point.ResultKind,
        isEnabled = point.IsEnabled ? 1 : 0,
        sortOrder = point.SortOrder,
        updatedAtUtc = point.UpdatedAtUtc.ToString("O"),
        id = point.Id
    }, ct);

    /// <summary>
    /// 删除试验项点。
    /// </summary>
    public Task DeleteAsync(int id, CancellationToken ct = default) => ExecuteAsync(
        "DELETE FROM test_item_points WHERE id=@id", new { id }, ct);

    /// <summary>
    /// 统计项点被型号配置引用的数量。
    /// </summary>
    public async Task<int> CountModelReferencesAsync(int pointId, CancellationToken ct = default)
    {
        var value = await ScalarAsync("SELECT COUNT(1) FROM model_point_configs WHERE test_item_point_id=@id", new { id = pointId }, ct);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 统计项点被执行结果引用的数量。
    /// </summary>
    public async Task<int> CountResultReferencesAsync(int pointId, CancellationToken ct = default)
    {
        var value = await ScalarAsync("SELECT COUNT(1) FROM test_item_results WHERE test_item_point_id=@id", new { id = pointId }, ct);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 把查询结果转换为试验项点对象。
    /// </summary>
    private static TestItemPoint Map(TestItemPointRow row) => new()
    {
        Id = row.Id,
        ProductTypeId = row.ProductTypeId,
        Name = row.Name,
        ExecutorCode = row.ExecutorCode,
        ResultKind = row.ResultKind,
        IsEnabled = row.IsEnabled != 0,
        SortOrder = row.SortOrder,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc),
        UpdatedAtUtc = ParseUtc(row.UpdatedAtUtc)
    };

    private const string PointSelect = "SELECT id AS Id, product_type_id AS ProductTypeId, name AS Name, executor_code AS ExecutorCode, result_kind AS ResultKind, is_enabled AS IsEnabled, sort_order AS SortOrder, created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc FROM test_item_points";

    private sealed class TestItemPointRow
    {
        public int Id { get; set; }
        public int ProductTypeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ExecutorCode { get; set; } = string.Empty;
        public string ResultKind { get; set; } = string.Empty;
        public int IsEnabled { get; set; }
        public int SortOrder { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }
}
