using XXX.TestBench.Core.Domain.TestPoints;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 试验项点持久化边界：项点归属于产品类型，运行时可在类型下增删改。
/// </summary>
public interface ITestPointRepository
{
    /// <summary>
    /// 按编号读取试验项点。
    /// </summary>
    Task<TestItemPoint?> GetAsync(int id, CancellationToken ct = default);
    /// <summary>
    /// 按产品类型读取试验项点列表。
    /// </summary>
    Task<IReadOnlyList<TestItemPoint>> ListByTypeAsync(int productTypeId, bool includeDisabled, CancellationToken ct = default);
    /// <summary>
    /// 新增试验项点。
    /// </summary>
    Task AddAsync(TestItemPoint point, CancellationToken ct = default);
    /// <summary>
    /// 更新试验项点。
    /// </summary>
    Task UpdateAsync(TestItemPoint point, CancellationToken ct = default);
    /// <summary>
    /// 删除试验项点。
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>
    /// 统计项点被型号配置引用的数量。
    /// </summary>
    Task<int> CountModelReferencesAsync(int pointId, CancellationToken ct = default);
    /// <summary>
    /// 统计项点被执行结果引用的数量。
    /// </summary>
    Task<int> CountResultReferencesAsync(int pointId, CancellationToken ct = default);
}
