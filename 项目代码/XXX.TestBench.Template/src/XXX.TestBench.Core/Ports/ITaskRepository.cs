using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.Core.Ports;

public interface ITaskRepository
{
    Task<TestTask?> GetAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsTaskNumberAsync(string taskNumber, CancellationToken ct = default);
    Task<TestTask?> GetActiveRunningAsync(CancellationToken ct = default);
    Task AddAsync(TestTask task, CancellationToken ct = default);
    Task UpdateAsync(TestTask task, CancellationToken ct = default);
    Task AddRecordAsync(TestRecord record, CancellationToken ct = default);
    Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default);
}
