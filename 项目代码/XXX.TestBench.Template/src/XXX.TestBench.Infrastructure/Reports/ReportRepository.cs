using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
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
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO report_records (test_record_id, template_path, output_path, status, error, created_by_user_id, created_at_utc, completed_at_utc)
            VALUES (@testRecordId, @templatePath, @outputPath, @status, @error, @createdByUserId, @createdAtUtc, @completedAtUtc)
            """, new
        {
            testRecordId = record.TestRecordId,
            templatePath = record.TemplatePath,
            outputPath = DbValue(record.OutputPath),
            status = (int)record.Status,
            error = DbValue(record.Error),
            createdByUserId = record.CreatedByUserId,
            createdAtUtc = record.CreatedAtUtc.ToString("O"),
            completedAtUtc = DbValue(record.CompletedAtUtc?.ToString("O"))
        }, ct);
        record.Id = (int)id;
    }

    /// <summary>
    /// 更新报表状态与结果信息。
    /// </summary>
    public Task UpdateAsync(ReportRecord record, CancellationToken ct = default) => ExecuteAsync(
        "UPDATE report_records SET output_path=@outputPath, status=@status, error=@error, completed_at_utc=@completedAtUtc WHERE id=@id",
        new
        {
            outputPath = DbValue(record.OutputPath),
            status = (int)record.Status,
            error = DbValue(record.Error),
            completedAtUtc = DbValue(record.CompletedAtUtc?.ToString("O")),
            id = record.Id
        }, ct);

    /// <summary>
    /// 按编号读取报表记录。
    /// </summary>
    public async Task<ReportRecord?> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ReportRow>(ReportSelect + " WHERE id=@id", new { id }, ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 按试验记录读取其全部报表记录。
    /// </summary>
    public async Task<IReadOnlyList<ReportRecord>> ListByRecordAsync(int testRecordId, CancellationToken ct = default)
    {
        var rows = await QueryAsync<ReportRow>(ReportSelect + " WHERE test_record_id=@testRecordId ORDER BY created_at_utc DESC, id DESC", new { testRecordId }, ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 把查询结果转换为报表记录对象。
    /// </summary>
    private static ReportRecord Map(ReportRow row) => new()
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

    /// <summary>
    /// 报表表常用查询字段。
    /// </summary>
    private const string ReportSelect = "SELECT id AS Id, test_record_id AS TestRecordId, template_path AS TemplatePath, output_path AS OutputPath, status AS Status, error AS Error, created_by_user_id AS CreatedByUserId, created_at_utc AS CreatedAtUtc, completed_at_utc AS CompletedAtUtc FROM report_records";

    private sealed class ReportRow
    {
        public int Id { get; set; }
        public int TestRecordId { get; set; }
        public string TemplatePath { get; set; } = string.Empty;
        public string? OutputPath { get; set; }
        public int Status { get; set; }
        public string? Error { get; set; }
        public int CreatedByUserId { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
        public string? CompletedAtUtc { get; set; }
    }
}
