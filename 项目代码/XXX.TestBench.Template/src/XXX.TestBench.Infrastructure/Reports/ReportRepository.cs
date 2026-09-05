using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;
using XXX.TestBench.Infrastructure.Persistence.Repositories;

namespace XXX.TestBench.Infrastructure.Reports;

/// <summary>
/// 报表记录的数据库实现。
/// </summary>
public sealed class ReportRepository : SqliteRepositoryBase, IReportRepository
{
    /// <summary>
    /// 创建报表仓库。
    /// </summary>
    public ReportRepository(ISqliteConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 新增报表记录并回填新编号。
    /// </summary>
    public async Task AddAsync(ReportRecord record, CancellationToken ct = default)
    {
        var id = await InsertIdentityAsync(new SqliteReportRecord
        {
            TestRecordId = record.TestRecordId,
            TemplatePath = record.TemplatePath,
            OutputPath = record.OutputPath,
            Status = (int)record.Status,
            Error = record.Error,
            CreatedByUserId = record.CreatedByUserId,
            CreatedAtUtc = record.CreatedAtUtc.ToString("O"),
            CompletedAtUtc = record.CompletedAtUtc?.ToString("O")
        }, ct);
        record.Id = checked((int)id);
    }

    /// <summary>
    /// 更新报表状态与结果信息。
    /// </summary>
    public Task UpdateAsync(ReportRecord record, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            Update<SqliteReportRecord>()
                .Where(x => x.Id == record.Id)
                .Set(x => x.OutputPath, record.OutputPath)
                .Set(x => x.Status, (int)record.Status)
                .Set(x => x.Error, record.Error)
                .Set(x => x.CompletedAtUtc, record.CompletedAtUtc?.ToString("O"))
                .ExecuteAffrows();
        }, ct);

    /// <summary>
    /// 按编号读取报表记录。
    /// </summary>
    public async Task<ReportRecord?> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await RunDbAsync(() => Select<SqliteReportRecord>()
            .Where(x => x.Id == id)
            .ToOne(), ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 按试验记录读取其全部报表记录。
    /// </summary>
    public async Task<IReadOnlyList<ReportRecord>> ListByRecordAsync(int testRecordId, CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() => Select<SqliteReportRecord>()
            .Where(x => x.TestRecordId == testRecordId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .OrderByDescending(x => x.Id)
            .ToList(), ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 把查询结果转换为报表记录对象。
    /// </summary>
    private static ReportRecord Map(SqliteReportRecord row) => new()
    {
        Id = row.Id,
        TestRecordId = row.TestRecordId,
        TemplatePath = row.TemplatePath,
        OutputPath = row.OutputPath,
        Status = (ReportStatus)row.Status,
        Error = row.Error,
        CreatedByUserId = row.CreatedByUserId,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc),
        CompletedAtUtc = ParseNullableUtc(row.CompletedAtUtc)
    };
}
