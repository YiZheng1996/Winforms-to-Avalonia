using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;

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
        var row = await RunDbAsync(() => Select<SqliteTestItemPoint>()
            .Where(x => x.Id == id)
            .ToOne(), ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 按产品类型读取试验项点列表。
    /// </summary>
    public async Task<IReadOnlyList<TestItemPoint>> ListByTypeAsync(int productTypeId, bool includeDisabled, CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() =>
        {
            var query = Select<SqliteTestItemPoint>()
                .Where(x => x.ProductTypeId == productTypeId);
            if (!includeDisabled)
                query = query.Where(x => x.IsEnabled == 1);
            return query
                .OrderByDescending(x => x.CreatedAtUtc)
                .OrderByDescending(x => x.Id)
                .ToList();
        }, ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 新增试验项点并回填编号。
    /// </summary>
    public async Task AddAsync(TestItemPoint point, CancellationToken ct = default)
    {
        var id = await InsertIdentityAsync(new SqliteTestItemPoint
        {
            ProductTypeId = point.ProductTypeId,
            Code = $"__legacy_point_{Guid.NewGuid():N}",
            Name = point.Name,
            ExecutorCode = point.ExecutorCode,
            ResultKind = point.ResultKind,
            IsEnabled = point.IsEnabled ? 1 : 0,
            SortOrder = point.SortOrder,
            CreatedAtUtc = point.CreatedAtUtc.ToString("O"),
            UpdatedAtUtc = point.UpdatedAtUtc.ToString("O")
        }, ct);
        point.Id = checked((int)id);
    }

    /// <summary>
    /// 更新试验项点。
    /// </summary>
    public Task UpdateAsync(TestItemPoint point, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            Update<SqliteTestItemPoint>()
                .Where(x => x.Id == point.Id)
                .Set(x => x.Name, point.Name)
                .Set(x => x.ExecutorCode, point.ExecutorCode)
                .Set(x => x.ResultKind, point.ResultKind)
                .Set(x => x.IsEnabled, point.IsEnabled ? 1 : 0)
                .Set(x => x.SortOrder, point.SortOrder)
                .Set(x => x.UpdatedAtUtc, point.UpdatedAtUtc.ToString("O"))
                .ExecuteAffrows();
        }, ct);

    /// <summary>
    /// 删除试验项点。
    /// </summary>
    public Task DeleteAsync(int id, CancellationToken ct = default)
        => RunDbAsync(() => Delete<SqliteTestItemPoint>()
            .Where(x => x.Id == id)
            .ExecuteAffrows(), ct);

    /// <summary>
    /// 统计项点被型号配置引用的数量。
    /// </summary>
    public Task<int> CountModelReferencesAsync(int pointId, CancellationToken ct = default)
        => RunDbAsync(() => checked((int)Select<SqliteModelPointConfig>()
            .Where(x => x.TestItemPointId == pointId)
            .Count()), ct);

    /// <summary>
    /// 统计项点被执行结果引用的数量。
    /// </summary>
    public Task<int> CountResultReferencesAsync(int pointId, CancellationToken ct = default)
        => RunDbAsync(() => checked((int)Select<SqliteTestItemResult>()
            .Where(x => x.TestItemPointId == pointId)
            .Count()), ct);

    /// <summary>
    /// 把查询结果转换为试验项点对象。
    /// </summary>
    private static TestItemPoint Map(SqliteTestItemPoint row) => new()
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
}
