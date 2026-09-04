using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 报表记录的数据仓库接口。
/// </summary>
public interface IReportRepository
{
    /// <summary>
    /// 新增报表记录。
    /// </summary>
    Task AddAsync(ReportRecord record, CancellationToken ct = default);
    /// <summary>
    /// 更新报表记录。
    /// </summary>
    Task UpdateAsync(ReportRecord record, CancellationToken ct = default);
    /// <summary>
    /// 按编号读取报表记录。
    /// </summary>
    Task<ReportRecord?> GetAsync(int id, CancellationToken ct = default);
    /// <summary>
    /// 按试验记录读取其全部报表记录。
    /// </summary>
    Task<IReadOnlyList<ReportRecord>> ListByRecordAsync(int testRecordId, CancellationToken ct = default);
}
