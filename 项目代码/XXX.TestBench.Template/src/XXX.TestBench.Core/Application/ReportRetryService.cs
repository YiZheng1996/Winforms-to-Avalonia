using System.Security.Cryptography;
using System.Text;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>按有效会话重试失败报告；固定请求标识保证双击和提交回执丢失时不会新建多个版本。</summary>
public sealed class ReportRetryService(
    AuthenticationService authentication, IUserRepository users, IReportRepository reports,
    IUnitOfWorkFactory transactions, IAuditLog audit, string ownerNodeId, TimeProvider time)
{
    public async Task<ReportRecord> RetryAsync(int failedReportId, string sessionToken, CancellationToken ct = default)
    {
        var actor = await authentication.BuildUserContextAsync(sessionToken, ct);
        var user = await users.GetByIdAsync(actor.UserId, ct);
        if (user is null || !user.IsEnabled || user.MustChangePassword
            || user.LockedUntilUtc > time.GetUtcNow().UtcDateTime)
            throw new AuthorizationException("账号不可用，请重新登录。");
        actor.EnsurePermission(PermissionCode.GenerateReports);
        var source = await reports.GetAsync(failedReportId, ct)
            ?? throw new DomainException("报表记录不存在。");
        if (source.Status != ReportStatus.Failed || source.OwnerNodeId != ownerNodeId
            || !Guid.TryParseExact(source.RevisionId, "D", out _))
            throw new DomainException("仅允许重试本节点有冻结输入的失败报表。");
        var requestId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("report-retry:" + source.RevisionId))
            .AsSpan(0, 16)).ToString("D");
        // 返回同一请求之前已提交的版本；不会对已成功的新版本再次计算。
        var existing = await reports.GetByRequestIdAsync(requestId, ct);
        if (existing is not null) return existing;
        var versions = await reports.ListByRecordAsync(source.TestRecordId, ct);
        if (versions.Any(r => r.Status is ReportStatus.Completed or ReportStatus.Pending or ReportStatus.Generating))
            throw new DomainException("该试验已有成功或处理中报表，请查看现有版本。");
        if (versions.Max(r => r.RevisionNo ?? 0) != source.RevisionNo)
            throw new DomainException("请选择最新的失败版本重试。");
        var pending = new ReportRecord
        {
            TestRecordId = source.TestRecordId, TemplatePath = source.TemplatePath,
            RevisionId = requestId, RequestId = requestId, Trigger = "ManualRetry", OwnerNodeId = ownerNodeId,
            InputHash = source.InputHash, TemplateHash = source.TemplateHash, MappingHash = source.MappingHash,
            Purpose = source.Purpose, Status = ReportStatus.Pending, CreatedByUserId = actor.UserId,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        };
        await using var transaction = transactions.Create();
        await transaction.BeginTransactionAsync(ct);
        var result = await reports.CreatePendingAsync(pending, ct);
        await audit.WriteAsync(actor.LoginName, "ReportRetry", source.RevisionId,
            $"失败版本 {source.RevisionNo}；新请求 {requestId}", ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
