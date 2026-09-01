using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.Core.Ports;

public interface IReportRepository
{
    Task AddAsync(ReportRecord record, CancellationToken ct = default);
    Task UpdateAsync(ReportRecord record, CancellationToken ct = default);
    Task<ReportRecord?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ReportRecord>> ListByRecordAsync(int testRecordId, CancellationToken ct = default);
}
