using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.Core.Ports;

public interface ITaskRepository
{
    Task<TestTask?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<TestTask>> ListTasksAsync(int? state, CancellationToken ct = default);
    Task<bool> ExistsTaskNumberAsync(string taskNumber, CancellationToken ct = default);
    Task<TestTask?> GetActiveRunningAsync(CancellationToken ct = default);
    Task AddAsync(TestTask task, CancellationToken ct = default);
    Task UpdateAsync(TestTask task, CancellationToken ct = default);
    Task AddRecordAsync(TestRecord record, CancellationToken ct = default);
    Task<TestRecord?> GetRecordAsync(int recordId, CancellationToken ct = default);
    Task<IReadOnlyList<TestRecord>> ListRecordsAsync(int? state, CancellationToken ct = default);
    Task UpdateRecordAsync(TestRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<TestItemResult>> ListItemResultsAsync(int recordId, CancellationToken ct = default);
    Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default);
    Task UpdateItemResultAsync(TestItemResult result, CancellationToken ct = default);
}
