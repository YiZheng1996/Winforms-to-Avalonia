using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 报表生成：从已保存的 TestRecord/TestItemResult/参数快照生成报表数据并调用 IReportGenerator。
/// Excel 不参与任务或运行参数输入；生成失败不回滚试验记录，可重试。
/// </summary>
public sealed class ReportService
{
    private readonly ITaskRepository _tasks;
    private readonly IProductRepository _products;
    private readonly ITestDefinitionRepository _definitions;
    private readonly IUserRepository _users;
    private readonly IReportRepository _reports;
    private readonly IReportGenerator _generator;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public ReportService(ITaskRepository tasks, IProductRepository products,
        ITestDefinitionRepository definitions, IUserRepository users, IReportRepository reports,
        IReportGenerator generator, IClock clock, IAuditLog audit)
    {
        _tasks = tasks;
        _products = products;
        _definitions = definitions;
        _users = users;
        _reports = reports;
        _generator = generator;
        _clock = clock;
        _audit = audit;
    }

    public async Task<ReportRecord> GenerateAsync(UserContext actor, int recordId, string templatePath, string outputDirectory, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.GenerateReports);
        var record = await _tasks.GetRecordAsync(recordId, ct) ?? throw new DomainException("试验记录不存在");
        if (record.State != Domain.Tasks.RecordState.Completed)
            throw new DomainException("只有已完成的试验记录可以生成报表");
        var task = await _tasks.GetAsync(record.TaskId, ct) ?? throw new DomainException("任务不存在");
        var model = await _products.GetModelAsync(task.ProductModelId, ct);
        var operatorUser = await _users.GetByIdAsync(record.OperatorUserId, ct);
        var results = await _tasks.ListItemResultsAsync(recordId, ct);

        var rows = new List<ReportItemRow>();
        foreach (var result in results.OrderBy(r => r.Id))
        {
            var definition = await _definitions.GetItemAsync(result.TestItemDefinitionId, ct);
            rows.Add(new ReportItemRow(rows.Count + 1, definition?.Name ?? result.TestItemDefinitionId.ToString(),
                result.SummaryValue ?? string.Empty, result.ResultText ?? string.Empty, result.State.ToString()));
        }

        var data = new ReportData(
            record.Id,
            task.TaskNumber,
            task.ProductIdentity.ProductNumber ?? string.Empty,
            model?.Code ?? string.Empty,
            record.ParameterSnapshot is null ? "未固化" : "固定流程",
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
