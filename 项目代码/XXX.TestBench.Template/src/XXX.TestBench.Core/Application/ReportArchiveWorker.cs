using System.Text.Json;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

public enum ReportArchiveWorkStatus
{
    Completed = 0,
    Failed = 1,
    NotProcessable = 2,
    ClaimLost = 3,
    ManualAuthorizationRequired = 4,
    RecoveryConfirmationRequired = 5,
    ArchiveOutcomeUnknown = 6,
    DatabaseAssociationPendingRecovery = 7
}

public sealed record ReportArchiveWorkItemResult(
    string RevisionId,
    ReportArchiveWorkStatus Status,
    string Message,
    Exception? Error = null);

/// <summary>
/// 通用 Pending 报表归档 worker。它只恢复本节点 Automatic 任务，不自动接管 Generating，也不提供计算器默认实现。
/// </summary>
public sealed class ReportArchiveWorker
{
    private const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int MaximumCalculationResultJsonLength = 1_000_000;

    private readonly IReportRepository _reports;
    private readonly IRecordRepository _records;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IReportStorage _storage;
    private readonly IReportArtifactProducer _producer;
    private readonly string _ownerNodeId;
    private readonly string _databaseInstanceId;
    private readonly TimeProvider _timeProvider;

    public ReportArchiveWorker(
        IReportRepository reports,
        IRecordRepository records,
        IUnitOfWorkFactory unitOfWorkFactory,
        IReportStorage storage,
        IReportArtifactProducer producer,
        string ownerNodeId,
        string databaseInstanceId,
        TimeProvider? timeProvider = null)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        EnsureCanonicalGuid(ownerNodeId, nameof(ownerNodeId));
        EnsureCanonicalGuid(databaseInstanceId, nameof(databaseInstanceId));
        _ownerNodeId = ownerNodeId;
        _databaseInstanceId = databaseInstanceId;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 处理本节点待领取的 Automatic 任务。Generating 只报告恢复需求，不以超时自动抢占。
    /// </summary>
    public async Task<IReadOnlyList<ReportArchiveWorkItemResult>> ProcessPendingAsync(
        int maximumJobs = 20,
        CancellationToken cancellationToken = default)
    {
        if (maximumJobs is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumJobs), "每批处理数量必须在 1 到 100 之间。");

        var recoverable = await _reports.ListRecoverableAsync(_ownerNodeId, cancellationToken);
        var results = new List<ReportArchiveWorkItemResult>(Math.Min(maximumJobs, recoverable.Count));
        foreach (var report in recoverable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= maximumJobs) break;

            if (report.Status == ReportStatus.Generating)
            {
                results.Add(new ReportArchiveWorkItemResult(
                    report.RevisionId ?? string.Empty,
                    ReportArchiveWorkStatus.RecoveryConfirmationRequired,
                    "任务仍处于 Generating；必须先确认旧 worker 已退出，再进入显式恢复流程。"));
                continue;
            }

            if (report.Status != ReportStatus.Pending) continue;
            if (!string.Equals(report.Trigger, "Automatic", StringComparison.Ordinal))
            {
                results.Add(new ReportArchiveWorkItemResult(
                    report.RevisionId ?? string.Empty,
                    ReportArchiveWorkStatus.ManualAuthorizationRequired,
                    "后台恢复只处理已提交的 Automatic 任务；人工生成须经独立权限用例授权。"));
                continue;
            }

            results.Add(await ProcessOneAsync(report, cancellationToken));
        }

        return results;
    }

    private async Task<ReportArchiveWorkItemResult> ProcessOneAsync(
        ReportRecord candidate,
        CancellationToken cancellationToken)
    {
        var revisionIdText = candidate.RevisionId ?? string.Empty;
        if (!Guid.TryParseExact(revisionIdText, "D", out var revisionId)
            || !string.Equals(revisionIdText, revisionId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal))
            return new ReportArchiveWorkItemResult(
                revisionIdText, ReportArchiveWorkStatus.NotProcessable,
                "Pending 报表 RevisionId 不是规范的小写 GUID；任务未领取，须人工核查。");

        var attemptToken = Guid.NewGuid().ToString("D").ToLowerInvariant();
        bool claimed;
        try
        {
            claimed = await _reports.TryClaimAsync(
                revisionIdText, _ownerNodeId, ReportStatus.Pending, attemptToken, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            try
            {
                var persisted = await _reports.GetAsync(candidate.Id, CancellationToken.None);
                if (IsClaimedBy(persisted, revisionIdText, attemptToken))
                    return new ReportArchiveWorkItemResult(
                        revisionIdText, ReportArchiveWorkStatus.RecoveryConfirmationRequired,
                        "领取提交后收到取消；任务保留 Generating，需确认本 worker 已退出。", ex);
            }
            catch (Exception verificationError)
            {
                return new ReportArchiveWorkItemResult(
                    revisionIdText, ReportArchiveWorkStatus.DatabaseAssociationPendingRecovery,
                    "领取取消后无法查证数据库状态；不得再次领取或启动计算。",
                    new AggregateException(ex, verificationError));
            }

            throw;
        }
        catch (Exception claimError)
        {
            try
            {
                var persisted = await _reports.GetAsync(candidate.Id, CancellationToken.None);
                if (!IsClaimedBy(persisted, revisionIdText, attemptToken))
                    return new ReportArchiveWorkItemResult(
                        revisionIdText, ReportArchiveWorkStatus.ClaimLost,
                        "领取调用返回异常；独立查证未发现本尝试已领取。", claimError);
                claimed = true;
            }
            catch (Exception verificationError)
            {
                return new ReportArchiveWorkItemResult(
                    revisionIdText, ReportArchiveWorkStatus.DatabaseAssociationPendingRecovery,
                    "领取调用和独立查证均失败；任务状态不确定，不能重复计算。",
                    new AggregateException(claimError, verificationError));
            }
        }

        if (!claimed)
            return new ReportArchiveWorkItemResult(
                revisionIdText, ReportArchiveWorkStatus.ClaimLost, "任务已被其他 worker 领取或状态已变化。");

        ReportRecord? report = null;
        var stage = "InputValidation";
        var publishAttempted = false;
        try
        {
            report = await _reports.GetAsync(candidate.Id, cancellationToken);
            if (!IsClaimedBy(report, revisionIdText, attemptToken))
                return new ReportArchiveWorkItemResult(
                    revisionIdText, ReportArchiveWorkStatus.ClaimLost,
                    "领取后无法核对当前尝试；任务保留为待恢复状态。");

            var record = await _records.GetRecordAsync(report.TestRecordId, cancellationToken)
                ?? throw new InvalidDataException("Pending 报表关联的试验记录不存在。");
            var frozenInput = ValidateFrozenInput(report, record, attemptToken);

            stage = "Calculation";
            var production = await _producer.ProduceAsync(
                new ReportArtifactProductionRequest(
                    revisionId, record.Id, report.InputHash!, record.ReportInputJson!, attemptToken),
                cancellationToken);

            if (!production.Succeeded)
            {
                await DisposeWorkbookAsync(production);
                EnsureProductionIdentity(production, report, revisionId);
                var error = new InvalidDataException(
                    string.IsNullOrWhiteSpace(production.Error) ? "报表计算适配器报告失败。" : production.Error);
                return await FailClaimedAsync(
                    report, attemptToken, NormalizeErrorStage(production.ErrorStage), error);
            }

            if (production.Workbook is null)
                throw new InvalidDataException("计算器报告成功，但没有提供 XLSX 产物。");
            if (production.Workbook.Content is null)
                throw new InvalidDataException("计算器返回的 XLSX 缺少可读内容流。");

            ReportEvaluation evaluation;
            ReportStorageStage storageStage;
            stage = "ArtifactValidation";
            await using (var workbookStream = production.Workbook.Content)
            {
                EnsureProductionIdentity(production, report, revisionId);
                evaluation = ValidateProduction(production, report, frozenInput);

                stage = "ArchiveStage";
                var workbook = production.Workbook with { Content = workbookStream };
                storageStage = await _storage.StageAsync(
                    revisionId, report.InputHash!, [workbook], cancellationToken);
            }

            ValidateStorageStage(storageStage, report, revisionId, production.Workbook);
            var requestedManifest = new ReportStorageManifest(
                1, revisionId, report.InputHash!, storageStage.StageKey, storageStage.Artifacts);
            stage = "ArchivePublish";
            publishAttempted = true;
            var publishedManifest = await _storage.PublishAsync(requestedManifest, cancellationToken);
            if (!SamePublishedContent(requestedManifest, publishedManifest))
                return new ReportArchiveWorkItemResult(
                    revisionIdText, ReportArchiveWorkStatus.ArchiveOutcomeUnknown,
                    "存储层返回的 manifest 与本次输入或产物不一致；任务保留待核查。");

            foreach (var artifact in publishedManifest.Artifacts)
            {
                var verification = await _storage.VerifyAsync(
                    artifact.StorageKey, artifact.Sha256, cancellationToken);
                if (!verification.Matches)
                    return new ReportArchiveWorkItemResult(
                        revisionIdText, ReportArchiveWorkStatus.ArchiveOutcomeUnknown,
                        $"已发布产物未通过回读校验（{artifact.LogicalName}）：{verification.Error ?? "摘要不匹配"}");
            }

            var manifestHash = ReportStorageManifestSerializer.ComputeSha256(publishedManifest);
            return await CommitCompletedAsync(
                report, record, attemptToken, publishedManifest, manifestHash, production,
                evaluation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 取消不代表旧 worker 已结束。保留 Generating，由调用方确认退出后再走显式恢复。
            throw;
        }
        catch (Exception ex)
        {
            if (publishAttempted)
                return new ReportArchiveWorkItemResult(
                    revisionIdText, ReportArchiveWorkStatus.ArchiveOutcomeUnknown,
                    "manifest 发布可能已完成；保留 Generating，禁止自动重算或覆盖。", ex);

            if (report is null)
                return new ReportArchiveWorkItemResult(
                    revisionIdText, ReportArchiveWorkStatus.DatabaseAssociationPendingRecovery,
                    "任务已领取，但读取当前尝试失败；保留 Generating，须先确认 worker 退出。", ex);

            return await FailClaimedAsync(report, attemptToken, stage, ex);
        }
    }

    private ReportInputSnapshotV1 ValidateFrozenInput(
        ReportRecord report,
        TestRecord record,
        string attemptToken)
    {
        if (report.Status != ReportStatus.Generating
            || !string.Equals(report.AttemptToken, attemptToken, StringComparison.Ordinal)
            || !string.Equals(report.OwnerNodeId, _ownerNodeId, StringComparison.Ordinal)
            || !string.Equals(report.Trigger, "Automatic", StringComparison.Ordinal)
            || !string.Equals(record.NodeId, _ownerNodeId, StringComparison.Ordinal)
            || !string.Equals(report.OwnerNodeId, record.NodeId, StringComparison.Ordinal)
            || report.CreatedByUserId != record.OperatorUserId
            || record.ResultContractVersion != 2
            || record.State == RecordState.Running
            || record.FinishedAtUtc is null
            || record.ReportInputJson is null
            || string.IsNullOrWhiteSpace(record.InputHash)
            || !string.Equals(report.InputHash, record.InputHash, StringComparison.Ordinal)
            || !string.Equals(report.ActiveAttemptKey, record.RunId, StringComparison.Ordinal))
            throw new InvalidDataException("报表任务与已提交终态、领取身份或活动 RunId 不匹配。");

        var inputHash = ReportInputSnapshotSerializer.ComputeHash(record.ReportInputJson);
        if (!string.Equals(inputHash, record.InputHash, StringComparison.Ordinal))
            throw new InvalidDataException("冻结报表输入原文的 SHA-256 与试验记录不一致。");

        var input = ReportInputSnapshotSerializer.Deserialize(record.ReportInputJson);
        if (!string.Equals(input.DatabaseInstanceId, _databaseInstanceId, StringComparison.Ordinal)
            || !string.Equals(input.RunId, record.RunId, StringComparison.Ordinal)
            || input.ExecutionState != record.State
            || input.Purpose != report.Purpose
            || input.Metadata.StartedAtUtc != record.StartedAtUtc
            || input.Metadata.FinishedAtUtc != record.FinishedAtUtc
            || !string.Equals(input.Metadata.RecordNumber, record.RecordNumber, StringComparison.Ordinal)
            || !string.Equals(input.Resources.TemplateHash, report.TemplateHash, StringComparison.Ordinal)
            || !string.Equals(input.Resources.MappingHash, report.MappingHash, StringComparison.Ordinal))
            throw new InvalidDataException("冻结报表输入与数据库、记录状态、时间或模板/映射摘要不一致。");

        var expectedPurpose = record.State == RecordState.Completed
            ? ReportPurpose.ProductEvaluation
            : ReportPurpose.ProcessOnly;
        if (report.Purpose != expectedPurpose)
            throw new InvalidDataException("报表用途与执行终态不匹配。");

        return input;
    }

    private static void EnsureProductionIdentity(
        ReportArtifactProductionResult production,
        ReportRecord report,
        Guid revisionId)
    {
        if (production.RevisionId != revisionId
            || !string.Equals(production.InputHash, report.InputHash, StringComparison.Ordinal))
            throw new InvalidDataException("计算结果属于其他 RevisionId 或冻结输入摘要。");
    }

    private static ReportEvaluation ValidateProduction(
        ReportArtifactProductionResult production,
        ReportRecord report,
        ReportInputSnapshotV1 frozenInput)
    {
        var definition = production.TemplateDefinition
            ?? throw new InvalidDataException("计算器未返回已验证的不可变模板映射定义。");
        var definitionErrors = definition.ValidateContract();
        if (definitionErrors.Count > 0)
            throw new InvalidDataException("模板映射定义无效：" + string.Join("；", definitionErrors));
        if (!string.Equals(definition.TemplateHash, report.TemplateHash, StringComparison.Ordinal)
            || !string.Equals(definition.MappingHash, report.MappingHash, StringComparison.Ordinal)
            || !string.Equals(definition.TemplateHash, frozenInput.Resources.TemplateHash, StringComparison.Ordinal)
            || !string.Equals(definition.MappingHash, frozenInput.Resources.MappingHash, StringComparison.Ordinal)
            || definition.Purpose != report.Purpose
            || !definition.SupportedRunScopes.Contains(frozenInput.Scope))
            throw new InvalidDataException("计算器使用的模板、映射、用途或执行范围与冻结版本不一致。");

        if (string.IsNullOrWhiteSpace(production.EngineName)
            || string.IsNullOrWhiteSpace(production.EngineVersion)
            || string.IsNullOrWhiteSpace(production.CalculationResultJson)
            || production.CalculationResultJson.Length > MaximumCalculationResultJsonLength)
            throw new InvalidDataException("计算器缺少引擎版本或结构化结果证据。");
        using (var document = JsonDocument.Parse(production.CalculationResultJson, new JsonDocumentOptions { MaxDepth = 32 }))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("计算结果证据必须是 JSON 对象。");
        }

        var workbook = production.Workbook
            ?? throw new InvalidDataException("计算器未返回 XLSX 工作簿。");
        if (workbook.Length < 0
            || !IsLowerSha256(workbook.Sha256)
            || string.IsNullOrWhiteSpace(workbook.LogicalName)
            || !workbook.LogicalName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(workbook.MediaType, XlsxMediaType, StringComparison.Ordinal)
            || !workbook.Content.CanRead)
            throw new InvalidDataException("计算器返回的 XLSX 文件名、媒体类型、长度、摘要或流无效。");

        if (report.Purpose == ReportPurpose.ProcessOnly)
        {
            if (definition.ProductConclusion is not null)
                throw new InvalidDataException("ProcessOnly 模板不能带产品合格/不合格映射。");
            return new ReportEvaluation(EvaluationState.NotEvaluated, null, "ProcessOnly");
        }

        var conclusion = definition.ProductConclusion
            ?? throw new InvalidDataException("产品判定模板缺少明确结论映射。");
        var rawConclusion = production.RawConclusion;
        if (string.IsNullOrWhiteSpace(rawConclusion))
            throw new InvalidDataException("计算器没有返回原始结论值。");
        if (conclusion.QualifiedValues.Contains(rawConclusion, StringComparer.Ordinal))
            return new ReportEvaluation(EvaluationState.Qualified, rawConclusion, "ReportTemplate");
        if (conclusion.UnqualifiedValues.Contains(rawConclusion, StringComparer.Ordinal))
            return new ReportEvaluation(EvaluationState.Unqualified, rawConclusion, "ReportTemplate");

        throw new InvalidDataException("Excel 原始结论不在模板声明的合格/不合格白名单内；不能推断产品判定。");
    }

    private static void ValidateStorageStage(
        ReportStorageStage stage,
        ReportRecord report,
        Guid revisionId,
        ReportArtifactContent workbook)
    {
        if (stage.RevisionId != revisionId
            || !string.Equals(stage.InputHash, report.InputHash, StringComparison.Ordinal)
            || stage.Artifacts.Count != 1)
            throw new InvalidDataException("存储暂存结果与本次 RevisionId、InputHash 或单 XLSX 产物不一致。");

        var artifact = stage.Artifacts[0];
        if (!string.Equals(artifact.LogicalName, workbook.LogicalName, StringComparison.Ordinal)
            || !string.Equals(artifact.StorageKey, $"reports/{revisionId:D}/{workbook.LogicalName}", StringComparison.Ordinal)
            || artifact.Length != workbook.Length
            || !string.Equals(artifact.Sha256, workbook.Sha256, StringComparison.Ordinal)
            || !string.Equals(artifact.MediaType, XlsxMediaType, StringComparison.Ordinal))
            throw new InvalidDataException("存储暂存描述与计算器返回的 XLSX 内容不一致。");
    }

    private async Task<ReportArchiveWorkItemResult> CommitCompletedAsync(
        ReportRecord report,
        TestRecord record,
        string attemptToken,
        ReportStorageManifest publishedManifest,
        string manifestHash,
        ReportArtifactProductionResult production,
        ReportEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var artifact = publishedManifest.Artifacts.Single();
        report.Status = ReportStatus.Completed;
        report.CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        report.StorageKey = artifact.StorageKey;
        report.OutputPath = null;
        report.ManifestHash = manifestHash;
        report.EngineName = production.EngineName;
        report.EngineVersion = production.EngineVersion;
        report.CalculationResultJson = production.CalculationResultJson;
        report.Error = null;
        report.ErrorStage = null;

        try
        {
            bool completed;
            await using (var unitOfWork = _unitOfWorkFactory.Create())
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken);
                completed = await _reports.TryCompleteAsync(
                    report,
                    attemptToken,
                    report.InputHash!,
                    evaluation.State,
                    evaluation.Text,
                    evaluation.ConclusionSource,
                    cancellationToken);
                if (!completed)
                    await unitOfWork.RollbackAsync(cancellationToken);
                else
                    await unitOfWork.CommitAsync(cancellationToken);
            }

            if (completed || await IsPersistedCompletionAsync(
                    report, record, publishedManifest, manifestHash, evaluation, CancellationToken.None))
                return new ReportArchiveWorkItemResult(
                    report.RevisionId!, ReportArchiveWorkStatus.Completed,
                    "报表 manifest 已发布，报表状态、产品结论和当前修订指针已事务关联。");

            return new ReportArchiveWorkItemResult(
                report.RevisionId!, ReportArchiveWorkStatus.DatabaseAssociationPendingRecovery,
                "归档已发布，但数据库条件更新未完成；保留 Generating，恢复时只补关联，不重新计算。");
        }
        catch (Exception ex)
        {
            try
            {
                if (await IsPersistedCompletionAsync(
                        report, record, publishedManifest, manifestHash, evaluation, CancellationToken.None))
                    return new ReportArchiveWorkItemResult(
                        report.RevisionId!, ReportArchiveWorkStatus.Completed,
                        "提交返回异常，但独立查证确认报表归档及记录指针均已提交。", ex);

                return new ReportArchiveWorkItemResult(
                    report.RevisionId!, ReportArchiveWorkStatus.DatabaseAssociationPendingRecovery,
                    "归档已发布但提交结果未能共同证明；保留恢复状态，不能重算或释放占用。", ex);
            }
            catch (Exception verifyError)
            {
                return new ReportArchiveWorkItemResult(
                    report.RevisionId!, ReportArchiveWorkStatus.DatabaseAssociationPendingRecovery,
                    "归档已发布，数据库查证也失败；保留恢复状态，不能重算或释放占用。",
                    new AggregateException(ex, verifyError));
            }
        }
    }

    private async Task<bool> IsPersistedCompletionAsync(
        ReportRecord report,
        TestRecord record,
        ReportStorageManifest manifest,
        string manifestHash,
        ReportEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var persistedReport = await _reports.GetAsync(report.Id, cancellationToken);
        var persistedRecord = await _records.GetRecordAsync(record.Id, cancellationToken);
        return persistedReport is { Status: ReportStatus.Completed }
               && persistedRecord is not null
               && string.Equals(persistedReport.RevisionId, manifest.RevisionId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal)
               && string.Equals(persistedReport.InputHash, manifest.InputHash, StringComparison.Ordinal)
               && string.Equals(persistedReport.StorageKey, manifest.Artifacts.Single().StorageKey, StringComparison.Ordinal)
               && string.Equals(persistedReport.ManifestHash, manifestHash, StringComparison.Ordinal)
               && persistedRecord.CurrentReportRevisionId == persistedReport.RevisionId
               && persistedRecord.InputHash == manifest.InputHash
               && persistedRecord.EvaluationState == evaluation.State
               && persistedRecord.EvaluationText == evaluation.Text
               && persistedRecord.ConclusionSource == evaluation.ConclusionSource;
    }

    private async Task<ReportArchiveWorkItemResult> FailClaimedAsync(
        ReportRecord report,
        string attemptToken,
        string stage,
        Exception error)
    {
        report.Status = ReportStatus.Failed;
        report.CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        report.ErrorStage = string.IsNullOrWhiteSpace(stage) ? "Worker" : stage;
        report.Error = LimitError(error.Message);
        try
        {
            var saved = await _reports.TryFailAsync(
                report, attemptToken, report.InputHash ?? string.Empty, CancellationToken.None);
            return saved
                ? new ReportArchiveWorkItemResult(
                    report.RevisionId ?? string.Empty, ReportArchiveWorkStatus.Failed,
                    $"报表处理在 {report.ErrorStage} 阶段失败；原试验记录和冻结输入已保留。", error)
                : new ReportArchiveWorkItemResult(
                    report.RevisionId ?? string.Empty, ReportArchiveWorkStatus.ClaimLost,
                    "报表失败结果未能通过当前尝试令牌提交；数据库状态未被覆盖。", error);
        }
        catch (Exception persistError)
        {
            return new ReportArchiveWorkItemResult(
                report.RevisionId ?? string.Empty, ReportArchiveWorkStatus.DatabaseAssociationPendingRecovery,
                "报表失败已发生，但失败状态提交不确定；须核对数据库后恢复。",
                new AggregateException(error, persistError));
        }
    }

    private static async Task DisposeWorkbookAsync(ReportArtifactProductionResult production)
    {
        if (production.Workbook?.Content is { } content)
            await content.DisposeAsync();
    }

    private static bool SamePublishedContent(ReportStorageManifest expected, ReportStorageManifest actual)
    {
        if (actual.SchemaVersion != expected.SchemaVersion
            || actual.RevisionId != expected.RevisionId
            || !string.Equals(actual.InputHash, expected.InputHash, StringComparison.Ordinal)
            || actual.Artifacts.Count != expected.Artifacts.Count)
            return false;

        return expected.Artifacts.OrderBy(item => item.LogicalName, StringComparer.Ordinal)
            .SequenceEqual(actual.Artifacts.OrderBy(item => item.LogicalName, StringComparer.Ordinal));
    }

    private static bool IsLowerSha256(string? value)
        => value is { Length: 64 }
           && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void EnsureCanonicalGuid(string value, string field)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed)
            || !string.Equals(value, parsed.ToString("D").ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException($"{field} 必须为小写 GUID D 格式。", field);
    }

    private bool IsClaimedBy(ReportRecord? report, string revisionId, string attemptToken)
        => report is { Status: ReportStatus.Generating }
           && string.Equals(report.RevisionId, revisionId, StringComparison.Ordinal)
           && string.Equals(report.OwnerNodeId, _ownerNodeId, StringComparison.Ordinal)
           && string.Equals(report.AttemptToken, attemptToken, StringComparison.Ordinal);

    private static string LimitError(string? message)
    {
        var value = string.IsNullOrWhiteSpace(message) ? "未提供错误详情。" : message.Trim();
        return value.Length <= 2000 ? value : value[..2000];
    }

    private static string NormalizeErrorStage(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage) || stage.Length > 64
            || stage.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
            return "Calculation";
        return stage;
    }

    private sealed record ReportEvaluation(
        EvaluationState State,
        string? Text,
        string ConclusionSource);
}
