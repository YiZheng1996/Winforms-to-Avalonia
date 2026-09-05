using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 报表生成：从已保存的试验记录/项点结果/参数快照生成报表数据并调用 IReportGenerator。
/// Excel 不参与执行或运行参数输入；生成失败不回滚试验记录，可重试。
/// </summary>
public sealed class ReportService
{
    /// <summary>
    /// 试验记录仓库。
    /// </summary>
    private readonly IRecordRepository _records;
    /// <summary>
    /// 产品数据仓库。
    /// </summary>
    private readonly IProductRepository _products;
    /// <summary>
    /// 用户仓库。
    /// </summary>
    private readonly IUserRepository _users;
    /// <summary>
    /// 报表记录仓库。
    /// </summary>
    private readonly IReportRepository _reports;
    /// <summary>
    /// 报表生成器。
    /// </summary>
    private readonly IReportGenerator _generator;
    /// <summary>
    /// 时间来源。
    /// </summary>
    private readonly IClock _clock;
    /// <summary>
    /// 审计日志。
    /// </summary>
    private readonly IAuditLog _audit;

    /// <summary>
    /// 创建报表服务。
    /// </summary>
    public ReportService(IRecordRepository records, IProductRepository products, IUserRepository users,
        IReportRepository reports, IReportGenerator generator, IClock clock, IAuditLog audit)
    {
        _records = records;
        _products = products;
        _users = users;
        _reports = reports;
        _generator = generator;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>
    /// 为已完成的试验记录生成报表，失败时保留记录并可重试。
    /// </summary>
    public async Task<ReportRecord> GenerateAsync(UserContext actor, int recordId, string templatePath, string outputDirectory, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.GenerateReports);
        // 只有已完成的记录才允许生成报表。
        var record = await _records.GetRecordAsync(recordId, ct) ?? throw new DomainException("试验记录不存在");
        if (record.State != RecordState.Completed)
            throw new DomainException("只有已完成的试验记录可以生成报表");
        var model = await _products.GetModelAsync(record.ProductModelId, ct)
            ?? throw new DomainException("试验记录关联的产品型号不存在");
        var operatorUser = await _users.GetByIdAsync(record.OperatorUserId, ct);
        var results = await _records.ListItemResultsAsync(recordId, ct);

        // 按固化序列逐项汇总结果，保证报表行与执行时一致。
        var sequence = SequenceSnapshot.FromJson(record.SequenceSnapshot) ?? new List<SequenceItem>();
        var rows = new List<ReportItemRow>();
        foreach (var item in sequence)
        {
            var result = results.FirstOrDefault(r => r.TestItemPointId == item.PointId);
            rows.Add(new ReportItemRow(item.SortOrder, item.Name,
                result?.SummaryValue ?? string.Empty,
                result?.ResultText ?? string.Empty,
                result?.State.ToString() ?? "未执行"));
        }

        var data = new ReportData(
            record.Id,
            record.RecordNumber,
            record.ProductIdentity.ProductNumber ?? string.Empty,
            model.Name,
            sequence.Count == 0 ? "未固化" : string.Join(" → ", sequence.Select(i => i.Name)),
            record.DeviceMode.ToString(),
            operatorUser?.DisplayName ?? record.OperatorUserId.ToString(),
            record.StartedAtUtc,
            record.Conclusion ?? string.Empty,
            rows);

        var report = new ReportRecord
        {
            TestRecordId = recordId,
            TemplatePath = templatePath,
            CreatedByUserId = actor.UserId,
            CreatedAtUtc = _clock.UtcNow
        };
        await _reports.AddAsync(report, ct);

        try
        {
            var outputPath = await _generator.GenerateAsync(data, templatePath, outputDirectory, ct);
            report.Status = ReportStatus.Completed;
            report.OutputPath = outputPath;
            report.CompletedAtUtc = _clock.UtcNow;
            await _reports.UpdateAsync(report, ct);
            await _audit.WriteAsync(actor.LoginName, "ReportGenerated", $"record:{recordId}", outputPath, ct);
        }
        catch (Exception ex)
        {
            report.Status = ReportStatus.Failed;
            report.Error = ex.Message;
            await _reports.UpdateAsync(report, ct);
            await _audit.WriteAsync(actor.LoginName, "ReportFailed", $"record:{recordId}", ex.Message, ct);
            throw new DomainException($"报表生成失败（试验记录已保留，可重试）：{ex.Message}");
        }
        return report;
    }

    /// <summary>
    /// 校验报表权限，越权时写入审计并抛出异常。
    /// </summary>
    private void Ensure(UserContext actor, Core.Domain.Identity.PermissionCode permission)
    {
        try { actor.EnsurePermission(permission); }
        catch (AuthorizationException)
        {
            _ = _audit.WriteAsync(actor.LoginName, "AccessDenied", permission.ToString(), null);
            throw;
        }
    }
}
