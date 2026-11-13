using System.Security.Cryptography;
using System.Text;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Records;
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
    /// 在终态记录事务中幂等创建一条新的 Pending 报表任务。
    /// </summary>
    public async Task<ReportRecord> CreatePendingAsync(ReportRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureTransaction();
        ValidatePendingRequest(record);

        var testRecord = await RunDbAsync(() => Select<SqliteTestRecord>()
            .Where(x => x.Id == record.TestRecordId)
            .ToOne(), ct);
        if (testRecord is null || testRecord.ResultContractVersion != 2
            || !Enum.IsDefined((RecordState)testRecord.State)
            || testRecord.State == (int)RecordState.Running
            || testRecord.NodeId != record.OwnerNodeId
            || string.IsNullOrWhiteSpace(testRecord.RunId)
            || string.IsNullOrWhiteSpace(testRecord.ReportInputJson)
            || !IsSha256(testRecord.InputHash)
            || !string.Equals(testRecord.InputHash, ComputeSha256(testRecord.ReportInputJson), StringComparison.Ordinal)
            || !string.Equals(testRecord.InputHash, record.InputHash, StringComparison.Ordinal))
            throw new DomainException("Pending 报表必须关联已保存终态及其完整、匹配的冻结输入");

        var existing = await GetByRequestIdAsync(record.RequestId!, ct);
        if (existing is not null)
        {
            if (!IsSameRequest(existing, record))
                throw new DomainException("报表请求标识已用于不同输入，拒绝重复创建");
            return existing;
        }

        if (record.ActiveAttemptKey is not null
            && !string.Equals(record.ActiveAttemptKey, testRecord.RunId, StringComparison.Ordinal))
            throw new DomainException("报表活动键与所属试验 RunId 不一致");

        var latest = await RunDbAsync(() => Select<SqliteReportRecord>()
            .Where(x => x.TestRecordId == record.TestRecordId)
            .Where(x => x.RevisionNo != null)
            .OrderByDescending(x => x.RevisionNo)
            .Limit(1)
            .ToOne(), ct);
        record.RevisionNo = checked((latest?.RevisionNo ?? 0) + 1);
        record.Status = ReportStatus.Pending;
        record.AttemptToken = null;
        record.ActiveAttemptKey = testRecord.RunId;
        record.RowVersion = 0;

        var id = await InsertIdentityAsync(new SqliteReportRecord
        {
            TestRecordId = record.TestRecordId,
            TemplatePath = record.TemplatePath,
            OutputPath = record.OutputPath,
            Status = (int)record.Status,
            Error = record.Error,
            CreatedByUserId = record.CreatedByUserId,
            CreatedAtUtc = record.CreatedAtUtc.ToString("O"),
            CompletedAtUtc = record.CompletedAtUtc?.ToString("O"),
            RevisionId = record.RevisionId,
            RevisionNo = record.RevisionNo,
            Trigger = record.Trigger,
            OwnerNodeId = record.OwnerNodeId,
            AttemptToken = record.AttemptToken,
            ActiveAttemptKey = record.ActiveAttemptKey,
            StorageKey = record.StorageKey,
            TemplateHash = record.TemplateHash,
            MappingHash = record.MappingHash,
            InputHash = record.InputHash,
            ManifestHash = record.ManifestHash,
            EngineName = record.EngineName,
            EngineVersion = record.EngineVersion,
            CalculationResultJson = record.CalculationResultJson,
            ErrorStage = record.ErrorStage,
            RequestId = record.RequestId,
            Purpose = record.Purpose is { } purpose ? (int)purpose : null,
            RowVersion = record.RowVersion,
            PdfStorageKey = record.PdfStorageKey,
            PdfArtifactId = record.PdfArtifactId,
            PdfHash = record.PdfHash,
            PdfManifestKey = record.PdfManifestKey,
            PdfState = (int)record.PdfState
        }, ct);
        record.Id = checked((int)id);
        return record;
    }

    /// <summary>
    /// 按请求标识读取，用于提交结果未知后的查证及幂等重试。
    /// </summary>
    public async Task<ReportRecord?> GetByRequestIdAsync(string requestId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var row = await RunDbAsync(() => Select<SqliteReportRecord>()
            .Where(x => x.RequestId == requestId)
            .ToOne(), ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 列出本节点待领取或待恢复确认的任务；不会自动接管 Generating 任务。
    /// </summary>
    public async Task<IReadOnlyList<ReportRecord>> ListRecoverableAsync(string ownerNodeId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerNodeId);
        var rows = await RunDbAsync(() => Select<SqliteReportRecord>()
            .Where(x => x.OwnerNodeId == ownerNodeId
                && (x.Status == (int)ReportStatus.Pending || x.Status == (int)ReportStatus.Generating))
            .OrderBy(x => x.CreatedAtUtc)
            .OrderBy(x => x.Id)
            .ToList(), ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 仅本节点可领取 Pending 任务；条件更新同时推进状态、尝试令牌和行版本。
    /// </summary>
    public async Task<bool> TryClaimAsync(
        string revisionId,
        string ownerNodeId,
        ReportStatus expectedStatus,
        string attemptToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptToken);
        if (expectedStatus != ReportStatus.Pending)
            throw new DomainException("首次领取只允许从 Pending 状态开始；Generating 恢复须先核实旧 worker 已退出");

        var row = await RunDbAsync(() => Select<SqliteReportRecord>()
            .Where(x => x.RevisionId == revisionId)
            .ToOne(), ct);
        if (row is null || row.OwnerNodeId != ownerNodeId || row.Status != (int)expectedStatus
            || row.ActiveAttemptKey is null || row.AttemptToken is not null)
            return false;

        var nextVersion = checked(row.RowVersion + 1);
        var affected = await RunDbAsync(() => Update<SqliteReportRecord>()
            .Where(x => x.Id == row.Id
                && x.OwnerNodeId == ownerNodeId
                && x.Status == (int)expectedStatus
                && x.AttemptToken == null
                && x.ActiveAttemptKey != null
                && x.RowVersion == row.RowVersion)
            .Set(x => x.Status, (int)ReportStatus.Generating)
            .Set(x => x.AttemptToken, attemptToken)
            .Set(x => x.RowVersion, nextVersion)
            .ExecuteAffrows(), ct);
        return affected == 1;
    }

    /// <summary>
    /// 只有当前尝试和冻结输入摘要都匹配时，才能提交产物并更新主记录版本指针。
    /// </summary>
    public async Task<bool> TryCompleteAsync(
        ReportRecord result,
        string attemptToken,
        string inputHash,
        EvaluationState evaluationState,
        string? evaluationText,
        string conclusionSource,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptToken);
        if (SqliteAmbient.Current is null)
            throw new DomainException("报表完成和主记录版本指针必须在同一工作单元事务中提交");
        if (result.Status != ReportStatus.Completed || string.IsNullOrWhiteSpace(result.RevisionId)
            || result.CompletedAtUtc is null || !IsSha256(inputHash)
            || !string.Equals(result.InputHash, inputHash, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(result.StorageKey) || !IsSha256(result.ManifestHash)
            || string.IsNullOrWhiteSpace(result.EngineName) || string.IsNullOrWhiteSpace(result.EngineVersion)
            || string.IsNullOrWhiteSpace(result.CalculationResultJson)
            || result.Purpose is null
            || !Enum.IsDefined(evaluationState)
            || evaluationState is EvaluationState.Pending or EvaluationState.Legacy
            || string.IsNullOrWhiteSpace(conclusionSource)
            || ((evaluationState is EvaluationState.Qualified or EvaluationState.Unqualified)
                && string.IsNullOrWhiteSpace(evaluationText))
            || (result.Purpose == ReportPurpose.ProductEvaluation
                && evaluationState is not (EvaluationState.Qualified or EvaluationState.Unqualified))
            || (result.Purpose == ReportPurpose.ProcessOnly
                && evaluationState is not (EvaluationState.NotEvaluated or EvaluationState.Error)))
            throw new DomainException("报表完成结果缺少归档、计算或输入摘要证据");

        var row = await RunDbAsync(() => Select<SqliteReportRecord>()
            .Where(x => x.RevisionId == result.RevisionId)
            .ToOne(), ct);
        if (row is null || row.TestRecordId != result.TestRecordId || row.Status != (int)ReportStatus.Generating
            || row.OwnerNodeId != result.OwnerNodeId || row.Purpose != (int)result.Purpose.Value || row.InputHash != inputHash
            || row.AttemptToken != attemptToken || row.ActiveAttemptKey is null
            || row.RowVersion != result.RowVersion)
            return false;

        var testRecord = await RunDbAsync(() => Select<SqliteTestRecord>()
            .Where(x => x.Id == row.TestRecordId)
            .ToOne(), ct);
        if (testRecord is null || testRecord.ResultContractVersion != 2
            || !Enum.IsDefined((RecordState)testRecord.State)
            || testRecord.State == (int)RecordState.Running || testRecord.InputHash != inputHash)
            throw new DomainException("报表完成时所属试验记录不再匹配冻结输入或终态");

        var nextReportVersion = checked(row.RowVersion + 1);
        var reportAffected = await RunDbAsync(() => Update<SqliteReportRecord>()
            .Where(x => x.Id == row.Id
                && x.OwnerNodeId == result.OwnerNodeId
                && x.Status == (int)ReportStatus.Generating
                && x.AttemptToken == attemptToken
                && x.InputHash == inputHash
                && x.ActiveAttemptKey != null
                && x.RowVersion == row.RowVersion)
            .Set(x => x.Status, (int)ReportStatus.Completed)
            .Set(x => x.OutputPath, result.OutputPath)
            .Set(x => x.Error, (string?)null)
            .Set(x => x.CompletedAtUtc, result.CompletedAtUtc.Value.ToString("O"))
            .Set(x => x.ActiveAttemptKey, (string?)null)
            .Set(x => x.StorageKey, result.StorageKey)
            .Set(x => x.ManifestHash, result.ManifestHash)
            .Set(x => x.EngineName, result.EngineName)
            .Set(x => x.EngineVersion, result.EngineVersion)
            .Set(x => x.CalculationResultJson, result.CalculationResultJson)
            .Set(x => x.ErrorStage, (string?)null)
            .Set(x => x.RowVersion, nextReportVersion)
            .ExecuteAffrows(), ct);
        if (reportAffected != 1) return false;

        var nextRecordVersion = checked(testRecord.RowVersion + 1);
        var recordAffected = await RunDbAsync(() => Update<SqliteTestRecord>()
            .Where(x => x.Id == testRecord.Id
                && x.ResultContractVersion == 2
                && x.State != (int)RecordState.Running
                && x.InputHash == inputHash
                && x.RowVersion == testRecord.RowVersion)
            .Set(x => x.CurrentReportRevisionId, result.RevisionId)
            .Set(x => x.EvaluationState, (int?)evaluationState)
            .Set(x => x.EvaluationText, evaluationText)
            .Set(x => x.ConclusionSource, conclusionSource)
            .Set(x => x.RowVersion, nextRecordVersion)
            .ExecuteAffrows(), ct);
        if (recordAffected != 1)
            throw new DomainException("主记录在报表完成时发生并发更新；必须回滚整个工作单元");

        result.RowVersion = nextReportVersion;
        result.ActiveAttemptKey = null;
        result.Status = ReportStatus.Completed;
        result.Error = null;
        result.ErrorStage = null;
        return true;
    }

    /// <summary>
    /// 当前尝试失败时结束占用；迟到或输入摘要不符的 worker 不会改写任务状态。
    /// </summary>
    public async Task<bool> TryFailAsync(
        ReportRecord result,
        string attemptToken,
        string inputHash,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptToken);
        if (result.Status != ReportStatus.Failed || string.IsNullOrWhiteSpace(result.RevisionId)
            || result.CompletedAtUtc is null || string.IsNullOrWhiteSpace(result.Error)
            || string.IsNullOrWhiteSpace(result.ErrorStage) || !IsSha256(inputHash)
            || !string.Equals(result.InputHash, inputHash, StringComparison.Ordinal))
            throw new DomainException("报表失败结果缺少阶段、原因或输入摘要");

        var row = await RunDbAsync(() => Select<SqliteReportRecord>()
            .Where(x => x.RevisionId == result.RevisionId)
            .ToOne(), ct);
        if (row is null || row.TestRecordId != result.TestRecordId || row.Status != (int)ReportStatus.Generating
            || row.OwnerNodeId != result.OwnerNodeId || row.InputHash != inputHash || row.AttemptToken != attemptToken
            || row.ActiveAttemptKey is null || row.RowVersion != result.RowVersion)
            return false;

        var nextVersion = checked(row.RowVersion + 1);
        var affected = await RunDbAsync(() => Update<SqliteReportRecord>()
            .Where(x => x.Id == row.Id
                && x.Status == (int)ReportStatus.Generating
                && x.OwnerNodeId == result.OwnerNodeId
                && x.AttemptToken == attemptToken
                && x.InputHash == inputHash
                && x.ActiveAttemptKey != null
                && x.RowVersion == row.RowVersion)
            .Set(x => x.Status, (int)ReportStatus.Failed)
            .Set(x => x.Error, result.Error)
            .Set(x => x.ErrorStage, result.ErrorStage)
            .Set(x => x.CompletedAtUtc, result.CompletedAtUtc.Value.ToString("O"))
            .Set(x => x.ActiveAttemptKey, (string?)null)
            .Set(x => x.RowVersion, nextVersion)
            .ExecuteAffrows(), ct);
        if (affected != 1) return false;

        result.RowVersion = nextVersion;
        result.ActiveAttemptKey = null;
        return true;
    }

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

    private static void EnsureTransaction()
    {
        if (SqliteAmbient.Current is null)
            throw new DomainException("创建 Pending 报表任务必须与试验终态处于同一工作单元事务");
    }

    private static void ValidatePendingRequest(ReportRecord record)
    {
        if (record.TestRecordId <= 0 || record.CreatedByUserId <= 0
            || string.IsNullOrWhiteSpace(record.TemplatePath)
            || string.IsNullOrWhiteSpace(record.RevisionId)
            || string.IsNullOrWhiteSpace(record.RequestId)
            || string.IsNullOrWhiteSpace(record.Trigger)
            || string.IsNullOrWhiteSpace(record.OwnerNodeId)
            || record.Purpose is null || !Enum.IsDefined(record.Purpose.Value)
            || record.Status != ReportStatus.Pending
            || record.AttemptToken is not null
            || !IsSha256(record.InputHash)
            || !IsSha256(record.TemplateHash)
            || !IsSha256(record.MappingHash))
            throw new DomainException("Pending 报表缺少版本、请求、所有者、用途或资源摘要合同");
    }

    private static bool IsSameRequest(ReportRecord existing, ReportRecord request)
        => existing.TestRecordId == request.TestRecordId
           && string.Equals(existing.RevisionId, request.RevisionId, StringComparison.Ordinal)
           && string.Equals(existing.RequestId, request.RequestId, StringComparison.Ordinal)
           && string.Equals(existing.InputHash, request.InputHash, StringComparison.Ordinal)
           && string.Equals(existing.TemplatePath, request.TemplatePath, StringComparison.Ordinal)
           && string.Equals(existing.TemplateHash, request.TemplateHash, StringComparison.Ordinal)
           && string.Equals(existing.MappingHash, request.MappingHash, StringComparison.Ordinal)
           && string.Equals(existing.Trigger, request.Trigger, StringComparison.Ordinal)
           && string.Equals(existing.OwnerNodeId, request.OwnerNodeId, StringComparison.Ordinal)
           && existing.Purpose == request.Purpose;

    private static string ComputeSha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool IsSha256(string? value)
    {
        if (value is not { Length: 64 }) return false;
        foreach (var character in value)
        {
            var isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isHex) return false;
        }
        return true;
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
        CompletedAtUtc = ParseNullableUtc(row.CompletedAtUtc),
        RevisionId = row.RevisionId,
        RevisionNo = row.RevisionNo,
        Trigger = row.Trigger,
        OwnerNodeId = row.OwnerNodeId,
        AttemptToken = row.AttemptToken,
        ActiveAttemptKey = row.ActiveAttemptKey,
        StorageKey = row.StorageKey,
        TemplateHash = row.TemplateHash,
        MappingHash = row.MappingHash,
        InputHash = row.InputHash,
        ManifestHash = row.ManifestHash,
        EngineName = row.EngineName,
        EngineVersion = row.EngineVersion,
        CalculationResultJson = row.CalculationResultJson,
        ErrorStage = row.ErrorStage,
        RequestId = row.RequestId,
        Purpose = row.Purpose is { } purpose ? (ReportPurpose)purpose : null,
        RowVersion = row.RowVersion,
        PdfStorageKey = row.PdfStorageKey,
        PdfArtifactId = row.PdfArtifactId,
        PdfHash = row.PdfHash,
        PdfManifestKey = row.PdfManifestKey,
        PdfState = (PdfState)row.PdfState
    };
}
