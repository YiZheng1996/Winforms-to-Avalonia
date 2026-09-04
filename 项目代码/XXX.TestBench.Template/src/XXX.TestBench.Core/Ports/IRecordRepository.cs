using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 试验记录/项点结果持久化边界。记录直接关联产品型号，不依赖任务。
/// </summary>
public interface IRecordRepository
{
    /// <summary>
    /// 判断记录流水号是否已存在。
    /// </summary>
    Task<bool> ExistsRecordNumberAsync(string recordNumber, CancellationToken ct = default);
    /// <summary>
    /// 新增试验记录。
    /// </summary>
    Task AddRecordAsync(TestRecord record, CancellationToken ct = default);
    /// <summary>
    /// 按编号读取试验记录。
    /// </summary>
    Task<TestRecord?> GetRecordAsync(int recordId, CancellationToken ct = default);
    /// <summary>
    /// 读取当前正在执行的记录。
    /// </summary>
    Task<TestRecord?> GetActiveRunningRecordAsync(CancellationToken ct = default);
    /// <summary>
    /// 按状态读取试验记录列表。
    /// </summary>
    Task<IReadOnlyList<TestRecord>> ListRecordsAsync(int? state, CancellationToken ct = default);
    /// <summary>
    /// 更新试验记录。
    /// </summary>
    Task UpdateRecordAsync(TestRecord record, CancellationToken ct = default);
    /// <summary>
    /// 读取某记录的项点结果列表。
    /// </summary>
    Task<IReadOnlyList<TestItemResult>> ListItemResultsAsync(int recordId, CancellationToken ct = default);
    /// <summary>
    /// 新增项点结果。
    /// </summary>
    Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default);
    /// <summary>
    /// 更新项点结果。
    /// </summary>
    Task UpdateItemResultAsync(TestItemResult result, CancellationToken ct = default);
}
