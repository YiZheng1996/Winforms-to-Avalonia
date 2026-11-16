using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 处理本机自动报表任务。任务领取、输入核对、计算、不可变归档和数据库关联按固定顺序执行；
/// Generating 只允许凭已发布的完整证据补关联，不重新领取或重算。
/// </summary>
public sealed class ReportGenerationWorker
{
    private const string AutomaticTrigger = "Automatic";
    private const string WorkbookLogicalName = "calculated.xlsx";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IReportRepository _reports;
    private readonly IReportCalculationRequestFactory _requestFactory;
    private readonly IReportCalculationEngine _engine;
    private readonly IReportStorage _storage;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;
    private readonly string _workRoot;
    // 同一实例的调度、恢复和重试计算共用此门；数据库 CAS 继续防止跨实例重复领取。
    private readonly SemaphoreSlim _processing = new(1, 1);

    public ReportGenerationWorker(
        IReportRepository reports,
        IReportCalculationRequestFactory requestFactory,
        IReportCalculationEngine engine,
        IReportStorage storage,
        IUnitOfWorkFactory unitOfWorkFactory,
        TimeProvider timeProvider,
        string workRoot)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        if (!Path.IsPathFullyQualified(workRoot))
            throw new ArgumentException("报表工作根目录必须是绝对路径。", nameof(workRoot));
        _workRoot = Path.GetFullPath(workRoot);
    }

    /// <summary>
    /// 串行处理本节点 Pending 自动/已授权重试任务。Generating 仅交给归档查证流程。
    /// </summary>
    public async Task<IReadOnlyList<ReportRecord>> ProcessPendingAsync(
        string ownerNodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerNodeId);
        await _processing.WaitAsync(cancellationToken);
        try
        {
        var candidates = await _reports.ListRecoverableAsync(ownerNodeId, cancellationToken);
        var results = new List<ReportRecord>();
        foreach (var candidate in candidates
                     .Where(item => item.Status == ReportStatus.Pending
                         && (item.Trigger == AutomaticTrigger || item.Trigger == "ManualRetry")
                         && string.Equals(item.OwnerNodeId, ownerNodeId, StringComparison.Ordinal))
                     .OrderBy(item => item.CreatedAtUtc)
                     .ThenBy(item => item.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProcessOneAsync(candidate, ownerNodeId, cancellationToken));
        }
        return results;
        }
        finally { _processing.Release(); }
    }

    /// <summary>
    /// 在安装级进程锁持有期间查证已发布归档，仅补数据库关联，绝不重新计算。
    /// 无归档、旧版无回执或损坏证据保持 Generating，交由后续人工核查。
    /// </summary>
    public async Task<IReadOnlyList<ReportRecord>> RecoverPublishedAsync(
        string ownerNodeId, string databaseInstanceId, IRecordRepository records,
        IReportArchiveReader archives, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerNodeId);
        if (!IsCanonicalGuid(databaseInstanceId)) throw new ArgumentException("数据库身份无效。", nameof(databaseInstanceId));
        await _processing.WaitAsync(ct);
        try
        {
            var recovered = new List<ReportRecord>();
            foreach (var report in (await _reports.ListRecoverableAsync(ownerNodeId, ct))
                         .Where(r => r.Status == ReportStatus.Generating && r.OwnerNodeId == ownerNodeId))
            {
                var manifest = await archives.ReadPublishedAsync(Guid.Parse(report.RevisionId!), ct);
                if (manifest is null || !manifest.Artifacts.Any(a => a.LogicalName == "completion.json")) continue;
                if (manifest.InputHash != report.InputHash) throw new InvalidDataException("恢复归档输入摘要不匹配。");
                var record = await records.GetRecordAsync(report.TestRecordId, ct)
                    ?? throw new InvalidDataException("恢复任务关联的试验记录不存在。");
                var input = await ReadArtifactAsync(manifest, "input.json", ct);
                var snapshot = ReportInputSnapshotSerializer.Deserialize(Encoding.UTF8.GetString(input));
                if (ComputeHash(input) != report.InputHash || record.InputHash != report.InputHash
                    || record.ReportInputJson != Encoding.UTF8.GetString(input)
                    || snapshot.DatabaseInstanceId != databaseInstanceId || snapshot.RunId != report.ActiveAttemptKey
                    || snapshot.RunId != record.RunId || snapshot.ExecutionState != record.State
                    || record.State == RecordState.Running || record.ResultContractVersion != 2
                    || snapshot.Purpose != report.Purpose || snapshot.Resources.TemplateHash != report.TemplateHash
                    || snapshot.Resources.MappingHash != report.MappingHash)
                    throw new InvalidDataException("恢复任务与冻结输入、数据库或试验终态不匹配。");
                var mapping = await ReadArtifactAsync(manifest, "mapping.json", ct);
                if (ComputeHash(mapping) != report.MappingHash
                    || manifest.Artifacts.Single(a => a.LogicalName == "template.xlsx").Sha256 != report.TemplateHash)
                    throw new InvalidDataException("恢复模板或映射摘要不匹配。");
                var definition = ReportTemplateMappingV1.Deserialize(mapping, report.TemplateHash!, report.MappingHash!);
                if (definition.Purpose != report.Purpose || !definition.SupportedRunScopes.Contains(snapshot.Scope))
                    throw new InvalidDataException("恢复模板用途或执行范围不匹配。");
                var receipt = JsonSerializer.Deserialize<ReportCompletionReceipt>(await ReadArtifactAsync(manifest, "completion.json", ct))
                    ?? throw new InvalidDataException("计算回执为空。");
                var calculation = receipt.Calculation;
                var workbook = manifest.Artifacts.Single(a => a.LogicalName == WorkbookLogicalName);
                if (receipt.SchemaVersion != 1 || receipt.AttemptToken != report.AttemptToken
                    || receipt.DatabaseInstanceId != databaseInstanceId || receipt.CompletedAtUtc.Kind != DateTimeKind.Utc
                    || receipt.CompletedAtUtc == default || calculation is null || !calculation.CalculationSucceeded
                    || calculation.RevisionId.ToString("D") != report.RevisionId || calculation.InputHash != report.InputHash
                    || calculation.CalculatedWorkbookHash != workbook.Sha256
                    || string.IsNullOrWhiteSpace(calculation.EngineName) || string.IsNullOrWhiteSpace(calculation.EngineVersion)
                    || calculation.PdfPath is not null || calculation.PdfHash is not null
                    || (report.Purpose == ReportPurpose.ProductEvaluation && calculation.ConclusionSource != "ExcelFormula"))
                    throw new InvalidDataException("计算回执与当前尝试不匹配，禁止恢复关联。");
                var evaluation = ResolveEvaluation(report.Purpose!.Value, definition, calculation.RawConclusion);
                var completed = CreateCompletedRecord(report, calculation, workbook.StorageKey,
                    ComputeHash(JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions)), receipt.CompletedAtUtc);
                await using var transaction = _unitOfWorkFactory.Create();
                await transaction.BeginTransactionAsync(ct);
                if (!await _reports.TryCompleteAsync(completed, report.AttemptToken!, report.InputHash!,
                        evaluation.State, evaluation.Text, evaluation.Source, ct))
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    continue;
                }
                await transaction.CommitAsync(ct);
                recovered.Add(completed);
            }
            return recovered;
        }
        finally { _processing.Release(); }
    }

    private async Task<byte[]> ReadArtifactAsync(ReportStorageManifest manifest, string name, CancellationToken ct)
    {
        var artifact = manifest.Artifacts.Single(a => a.LogicalName == name);
        await using var stream = await _storage.OpenReadAsync(artifact.StorageKey, ct);
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, ct);
        var bytes = content.ToArray();
        if (bytes.LongLength != artifact.Length || ComputeHash(bytes) != artifact.Sha256)
            throw new InvalidDataException($"恢复证据文件摘要不匹配：{name}");
        return bytes;
    }

    private async Task<ReportRecord> ProcessOneAsync(
        ReportRecord candidate,
        string ownerNodeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.RevisionId)
            || string.IsNullOrWhiteSpace(candidate.InputHash)
            || string.IsNullOrWhiteSpace(candidate.OwnerNodeId))
            return candidate;

        var attemptToken = Guid.NewGuid().ToString("D").ToLowerInvariant();
        if (!await _reports.TryClaimAsync(candidate.RevisionId, ownerNodeId, ReportStatus.Pending,
                attemptToken, cancellationToken))
            return await ReadCurrentAsync(candidate, cancellationToken);

        var claimed = await _reports.GetAsync(candidate.Id, cancellationToken)
            ?? throw new DomainException("报表领取后无法读取持久状态；保留任务供恢复核查。");
        if (claimed.Status != ReportStatus.Generating
            || !string.Equals(claimed.AttemptToken, attemptToken, StringComparison.Ordinal)
            || !string.Equals(claimed.OwnerNodeId, ownerNodeId, StringComparison.Ordinal)
            || !string.Equals(claimed.InputHash, candidate.InputHash, StringComparison.Ordinal))
            return claimed;

        var publishAttempted = false;
        ReportCalculationRequest? request = null;
        try
        {
            request = await _requestFactory.CreateAsync(claimed, cancellationToken);
            await ValidateRequestAsync(claimed, request, cancellationToken);
            var calculated = await _engine.CalculateAsync(request, cancellationToken);
            ValidateCalculationResult(request, calculated);

            if (!calculated.CalculationSucceeded)
                return await FailAsync(claimed, attemptToken, calculated.ErrorStage ?? "Calculation",
                    calculated.Error ?? "报表引擎未确认计算成功。", cancellationToken);

            var evaluation = ResolveEvaluation(claimed.Purpose!.Value, request.TemplateDefinition, calculated.RawConclusion);
            var files = await OpenArchiveFilesAsync(request, calculated, cancellationToken);
            var receipt = JsonSerializer.SerializeToUtf8Bytes(new ReportCompletionReceipt(
                1, attemptToken, request.DatabaseInstanceId, _timeProvider.GetUtcNow().UtcDateTime, calculated));
            files.Add(new ReportArtifactContent("completion.json", "application/json", receipt.Length,
                ComputeHash(receipt), new MemoryStream(receipt, writable: false)));
            ReportStorageStage stage;
            try
            {
                stage = await _storage.StageAsync(Guid.Parse(claimed.RevisionId!), claimed.InputHash!, files, cancellationToken);
            }
            finally
            {
                foreach (var file in files)
                    await file.Content.DisposeAsync();
            }

            ValidateStage(stage, claimed, files);
            var manifest = new ReportStorageManifest(
                1,
                Guid.Parse(claimed.RevisionId!),
                claimed.InputHash!,
                stage.StageKey,
                stage.Artifacts);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
            var manifestHash = ComputeHash(manifestBytes);

            // Publish 可能在返回前已完成文件发布；异常后保留 Generating，不能误标 Failed 或重复计算。
            publishAttempted = true;
            var published = await _storage.PublishAsync(manifest, cancellationToken);
            ValidatePublishedManifest(manifest, published);
            var workbook = published.Artifacts.Single(item => item.LogicalName == WorkbookLogicalName);
            var completed = CreateCompletedRecord(claimed, calculated, workbook.StorageKey, manifestHash,
                _timeProvider.GetUtcNow().UtcDateTime);

            await using var unitOfWork = _unitOfWorkFactory.Create();
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            var committed = await _reports.TryCompleteAsync(
                completed,
                attemptToken,
                claimed.InputHash!,
                evaluation.State,
                evaluation.Text,
                evaluation.Source,
                cancellationToken);
            if (!committed)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return await ReadCurrentAsync(claimed, CancellationToken.None);
            }
            await unitOfWork.CommitAsync(cancellationToken);
            return await ReadCurrentAsync(completed, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 取消不证明 worker 及文件发布已退出；保持 Generating 供启动恢复先查证进程状态。
            throw;
        }
        catch (Exception ex) when (!publishAttempted)
        {
            return await FailAsync(claimed, attemptToken, ClassifyFailureStage(ex), ex.Message, CancellationToken.None);
        }
        catch
        {
            // 文件 manifest 可能已发布，需按 RevisionId/InputHash 核查后再补关联，不能重算或覆盖。
            return await ReadCurrentAsync(claimed, CancellationToken.None);
        }
        finally
        {
            if (request is not null)
                TryDeleteAttemptDirectory(request, claimed);
        }
    }

    private async Task<ReportInputSnapshotV1> ValidateRequestAsync(
        ReportRecord report,
        ReportCalculationRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(report.RevisionId, "D", out var revisionId)
            || !string.Equals(report.RevisionId, revisionId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal)
            || !Guid.TryParseExact(report.AttemptToken, "D", out var attemptId)
            || !string.Equals(report.AttemptToken, attemptId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal)
            || request.RevisionId != revisionId
            || !string.Equals(request.AttemptToken, report.AttemptToken, StringComparison.Ordinal)
            || !string.Equals(request.InputHash, report.InputHash, StringComparison.Ordinal)
            || !IsCanonicalGuid(request.DatabaseInstanceId)
            || !string.Equals(request.TemplateDefinition.TemplateHash, report.TemplateHash, StringComparison.Ordinal)
            || !string.Equals(request.TemplateDefinition.MappingHash, report.MappingHash, StringComparison.Ordinal)
            || request.TemplateDefinition.Purpose != report.Purpose
            || request.GeneratePdf
            || request.Timeout <= TimeSpan.Zero)
            throw new InvalidDataException("报表计算请求与已领取记录、冻结资源或当前最小处理能力不匹配。");

        EnsureRequestWorkDirectory(request, report, revisionId, attemptId);

        var contractErrors = request.TemplateDefinition.ValidateContract();
        if (contractErrors.Count > 0)
            throw new InvalidDataException("报表模板映射合同无效：" + string.Join("；", contractErrors));

        var inputJson = await ReadUtf8FileAsync(request.InputJsonPath, ".json", cancellationToken);
        if (!string.Equals(ComputeHash(inputJson), report.InputHash, StringComparison.Ordinal))
            throw new InvalidDataException("冻结报表输入摘要不匹配。");
        var snapshot = ReportInputSnapshotSerializer.Deserialize(new UTF8Encoding(false, true).GetString(inputJson));
        if (!string.Equals(snapshot.DatabaseInstanceId, request.DatabaseInstanceId, StringComparison.Ordinal)
            || !string.Equals(snapshot.RunId, report.ActiveAttemptKey, StringComparison.Ordinal)
            || snapshot.Purpose != report.Purpose
            || snapshot.Resources.TemplateHash != report.TemplateHash
            || snapshot.Resources.MappingHash != report.MappingHash
            || !request.TemplateDefinition.SupportedRunScopes.Contains(snapshot.Scope))
            throw new InvalidDataException("冻结报表输入的台架数据库、RunId、用途或资源版本与任务不匹配。");

        var template = await ReadUtf8FileAsync(request.TemplatePath, ".xlsx", cancellationToken);
        var mapping = await ReadUtf8FileAsync(request.MappingPath, ".json", cancellationToken);
        if (!string.Equals(ComputeHash(template), report.TemplateHash, StringComparison.Ordinal)
            || !string.Equals(ComputeHash(mapping), report.MappingHash, StringComparison.Ordinal))
            throw new InvalidDataException("报表模板或映射文件摘要与试验时冻结版本不匹配。");
        return snapshot;
    }

    private static void ValidateCalculationResult(ReportCalculationRequest request, ReportCalculationResult result)
    {
        if (result.RevisionId != request.RevisionId
            || !string.Equals(result.InputHash, request.InputHash, StringComparison.Ordinal))
            throw new InvalidDataException("计算结果不属于当前报表版本或冻结输入。");
        if (!result.CalculationSucceeded)
            return;
        if (request.TemplateDefinition.Purpose == ReportPurpose.ProductEvaluation
            && !string.Equals(result.ConclusionSource, "ExcelFormula", StringComparison.Ordinal))
            throw new InvalidDataException("产品判定结果必须明确声明来自 Excel 公式计算。");
        if (string.IsNullOrWhiteSpace(result.EngineName) || string.IsNullOrWhiteSpace(result.EngineVersion)
            || string.IsNullOrWhiteSpace(result.CalculatedWorkbookPath)
            || !IsSha256(result.CalculatedWorkbookHash)
            || result.PdfPath is not null || result.PdfHash is not null)
            throw new InvalidDataException("计算结果缺少引擎、XLSX 摘要，或返回了当前未启用的 PDF 产物。");
    }

    private (EvaluationState State, string? Text, string Source) ResolveEvaluation(
        ReportPurpose purpose,
        ReportTemplateDefinition definition,
        string? rawConclusion)
    {
        if (purpose == ReportPurpose.ProcessOnly)
            return (EvaluationState.NotEvaluated, null, "ProcessOnly");

        var conclusion = definition.ProductConclusion
            ?? throw new InvalidDataException("产品判定模板缺少结论映射。");
        if (string.IsNullOrEmpty(rawConclusion))
            throw new InvalidDataException("产品判定报表没有原始结论文本。");
        if (conclusion.QualifiedValues.Contains(rawConclusion, StringComparer.Ordinal))
            return (EvaluationState.Qualified, rawConclusion, "ExcelFormula");
        if (conclusion.UnqualifiedValues.Contains(rawConclusion, StringComparer.Ordinal))
            return (EvaluationState.Unqualified, rawConclusion, "ExcelFormula");
        throw new InvalidDataException("Excel 原始结论不在模板声明的合格/不合格精确值清单中。");
    }

    private async Task<List<ReportArtifactContent>> OpenArchiveFilesAsync(
        ReportCalculationRequest request,
        ReportCalculationResult result,
        CancellationToken cancellationToken)
    {
        var outputPath = RequirePath(result.CalculatedWorkbookPath, ".xlsx");
        var inputPath = RequirePath(request.InputWorkbookPath, ".xlsx");
        var templatePath = RequirePath(request.TemplatePath, ".xlsx");
        var mappingPath = RequirePath(request.MappingPath, ".json");
        var inputJsonPath = RequirePath(request.InputJsonPath, ".json");
        var sourcePaths = new[] { inputPath, templatePath, mappingPath, inputJsonPath };
        if (sourcePaths.Contains(outputPath, OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal))
            throw new InvalidDataException("计算输出必须写入独立文件，不能覆盖冻结输入或资源。");
        EnsureCalculationOutputPath(request, inputPath, outputPath);

        var files = new List<ReportArtifactContent>();
        try
        {
            files.Add(await OpenArchiveFileAsync("input.json", "application/json", inputJsonPath, request.InputHash, cancellationToken));
            files.Add(await OpenArchiveFileAsync("template.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                templatePath, request.TemplateDefinition.TemplateHash, cancellationToken));
            files.Add(await OpenArchiveFileAsync("mapping.json", "application/json", mappingPath,
                request.TemplateDefinition.MappingHash, cancellationToken));
            files.Add(await OpenArchiveFileAsync("input.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                inputPath, null, cancellationToken));
            files.Add(await OpenArchiveFileAsync(WorkbookLogicalName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                outputPath, result.CalculatedWorkbookHash, cancellationToken));
            return files;
        }
        catch
        {
            foreach (var file in files)
                await file.Content.DisposeAsync();
            throw;
        }
    }

    private static async Task<ReportArtifactContent> OpenArchiveFileAsync(
        string logicalName,
        string mediaType,
        string path,
        string? expectedHash,
        CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (expectedHash is not null && !string.Equals(hash, expectedHash, StringComparison.Ordinal))
                throw new InvalidDataException($"归档文件摘要不匹配：{logicalName}");
            var length = stream.Length;
            if (length <= 0)
                throw new InvalidDataException($"归档文件为空：{logicalName}");
            stream.Position = 0;
            return new(logicalName, mediaType, length, hash, stream);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static void ValidateStage(ReportStorageStage stage, ReportRecord report,
        IReadOnlyList<ReportArtifactContent> files)
    {
        if (stage.RevisionId != Guid.Parse(report.RevisionId!)
            || !string.Equals(stage.InputHash, report.InputHash, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(stage.StageKey)
            || stage.Artifacts.Count != files.Count
            || stage.Artifacts.Select(item => item.LogicalName).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new InvalidDataException("报表暂存结果的版本、输入摘要或文件清单不完整。");

        foreach (var file in files)
        {
            var stored = stage.Artifacts.SingleOrDefault(item => item.LogicalName == file.LogicalName);
            if (stored is null || stored.Length != file.Length || stored.Sha256 != file.Sha256
                || string.IsNullOrWhiteSpace(stored.StorageKey))
                throw new InvalidDataException($"报表暂存文件摘要不一致：{file.LogicalName}");
        }
    }

    private static void ValidatePublishedManifest(ReportStorageManifest expected, ReportStorageManifest actual)
    {
        if (actual.SchemaVersion != expected.SchemaVersion
            || actual.RevisionId != expected.RevisionId
            || !string.Equals(actual.InputHash, expected.InputHash, StringComparison.Ordinal)
            || !string.Equals(actual.StageKey, expected.StageKey, StringComparison.Ordinal)
            || actual.Artifacts.Count != expected.Artifacts.Count)
            throw new InvalidDataException("发布后的报表 manifest 与本次暂存版本不一致。");

        foreach (var descriptor in expected.Artifacts)
        {
            var published = actual.Artifacts.SingleOrDefault(item => item.LogicalName == descriptor.LogicalName);
            if (published is null || published != descriptor)
                throw new InvalidDataException($"发布后的报表文件清单发生变化：{descriptor.LogicalName}");
        }
    }

    private async Task<ReportRecord> FailAsync(
        ReportRecord claimed,
        string attemptToken,
        string stage,
        string message,
        CancellationToken cancellationToken)
    {
        var failed = CopyRecord(claimed);
        failed.Status = ReportStatus.Failed;
        failed.ErrorStage = string.IsNullOrWhiteSpace(stage) ? "ReportProcessing" : stage;
        failed.Error = string.IsNullOrWhiteSpace(message) ? "报表生成失败。" : message;
        failed.CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var evaluationState = failed.Purpose == ReportPurpose.ProductEvaluation
            ? EvaluationState.Error
            : EvaluationState.NotEvaluated;

        await using var unitOfWork = _unitOfWorkFactory.Create();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        var saved = await _reports.TryFailAsync(failed, attemptToken, failed.InputHash!, evaluationState, cancellationToken);
        if (!saved)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            return await ReadCurrentAsync(claimed, CancellationToken.None);
        }
        await unitOfWork.CommitAsync(cancellationToken);
        return await ReadCurrentAsync(failed, CancellationToken.None);
    }

    private static ReportRecord CreateCompletedRecord(
        ReportRecord source,
        ReportCalculationResult calculation,
        string storageKey,
        string manifestHash,
        DateTime completedAtUtc)
    {
        var result = CopyRecord(source);
        result.Status = ReportStatus.Completed;
        result.CompletedAtUtc = completedAtUtc;
        result.StorageKey = storageKey;
        result.OutputPath = null;
        result.ManifestHash = manifestHash;
        result.EngineName = calculation.EngineName;
        result.EngineVersion = calculation.EngineVersion;
        result.CalculationResultJson = JsonSerializer.Serialize(calculation);
        result.Error = null;
        result.ErrorStage = null;
        result.PdfState = PdfState.NotRequested;
        return result;
    }

    private static ReportRecord CopyRecord(ReportRecord source) => new()
    {
        Id = source.Id,
        TestRecordId = source.TestRecordId,
        TemplatePath = source.TemplatePath,
        RevisionId = source.RevisionId,
        RevisionNo = source.RevisionNo,
        Trigger = source.Trigger,
        OwnerNodeId = source.OwnerNodeId,
        AttemptToken = source.AttemptToken,
        ActiveAttemptKey = source.ActiveAttemptKey,
        StorageKey = source.StorageKey,
        TemplateHash = source.TemplateHash,
        MappingHash = source.MappingHash,
        InputHash = source.InputHash,
        ManifestHash = source.ManifestHash,
        EngineName = source.EngineName,
        EngineVersion = source.EngineVersion,
        CalculationResultJson = source.CalculationResultJson,
        ErrorStage = source.ErrorStage,
        RequestId = source.RequestId,
        Purpose = source.Purpose,
        RowVersion = source.RowVersion,
        PdfStorageKey = source.PdfStorageKey,
        PdfArtifactId = source.PdfArtifactId,
        PdfHash = source.PdfHash,
        PdfManifestKey = source.PdfManifestKey,
        PdfState = source.PdfState,
        OutputPath = source.OutputPath,
        Status = source.Status,
        Error = source.Error,
        CreatedByUserId = source.CreatedByUserId,
        CreatedAtUtc = source.CreatedAtUtc,
        CompletedAtUtc = source.CompletedAtUtc
    };

    private async Task<ReportRecord> ReadCurrentAsync(ReportRecord fallback, CancellationToken cancellationToken)
        => await _reports.GetAsync(fallback.Id, cancellationToken) ?? fallback;

    private static async Task<byte[]> ReadUtf8FileAsync(string path, string extension, CancellationToken cancellationToken)
    {
        var fullPath = RequirePath(path, extension);
        return await File.ReadAllBytesAsync(fullPath, cancellationToken);
    }

    private static string RequirePath(string? path, string extension)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("报表任务路径必须是非空绝对路径。");
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath))
            throw new FileNotFoundException($"报表任务文件不存在或格式不受支持（要求 {extension}）。", fullPath);
        return fullPath;
    }

    private void EnsureRequestWorkDirectory(
        ReportCalculationRequest request,
        ReportRecord report,
        Guid revisionId,
        Guid attemptId)
    {
        var expected = Path.GetFullPath(Path.Combine(_workRoot,
            revisionId.ToString("N"), attemptId.ToString("N")));
        var actual = Path.GetFullPath(request.WorkDirectoryPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(expected, actual, comparison) || !Directory.Exists(actual)
            || !string.Equals(report.AttemptToken, request.AttemptToken, StringComparison.Ordinal))
            throw new InvalidDataException("报表尝试目录不属于当前版本和尝试令牌。");

        foreach (var path in new[]
                 {
                     request.InputJsonPath,
                     request.InputWorkbookPath,
                     request.TemplatePath,
                     request.MappingPath
                 })
        {
            if (!IsWithinDirectory(actual, path))
                throw new InvalidDataException("报表请求文件必须位于当前尝试的隔离工作目录内。");
        }
    }

    private void TryDeleteAttemptDirectory(ReportCalculationRequest request, ReportRecord report)
    {
        try
        {
            if (!Guid.TryParseExact(report.RevisionId, "D", out var revisionId)
                || !Guid.TryParseExact(report.AttemptToken, "D", out var attemptId))
                return;

            var expected = Path.GetFullPath(Path.Combine(_workRoot,
                revisionId.ToString("N"), attemptId.ToString("N")));
            var actual = Path.GetFullPath(request.WorkDirectoryPath);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(expected, actual, comparison)
                && request.RevisionId == revisionId
                && string.Equals(request.AttemptToken, report.AttemptToken, StringComparison.Ordinal)
                && Directory.Exists(actual))
                Directory.Delete(actual, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不覆盖计算或发布结果；启动恢复可清点遗留工作目录。
        }
        catch (UnauthorizedAccessException)
        {
            // 清理失败不覆盖计算或发布结果；启动恢复可清点遗留工作目录。
        }
    }

    private static bool IsWithinDirectory(string directory, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;
        var relative = Path.GetRelativePath(directory, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void EnsureCalculationOutputPath(
        ReportCalculationRequest request,
        string inputPath,
        string outputPath)
    {
        var inputDirectory = Path.GetDirectoryName(inputPath)!;
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        var workDirectory = Directory.GetParent(outputDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var expectedPrefix = $".report-engine-{request.RevisionId:N}-";

        if (!string.Equals(Path.GetFileName(outputDirectory), "output", StringComparison.Ordinal)
            || workDirectory is null
            || !string.Equals(Path.GetDirectoryName(workDirectory.FullName), inputDirectory, comparison)
            || !Path.GetFileName(workDirectory.FullName).StartsWith(expectedPrefix, comparison)
            || !string.Equals(Path.GetFileName(outputPath), Path.GetFileName(inputPath), comparison))
            throw new InvalidDataException("计算结果必须位于当前报表输入目录下的隔离 worker 任务目录。");
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsCanonicalGuid(string? value)
        => value is not null
            && Guid.TryParseExact(value, "D", out var id)
            && string.Equals(value, id.ToString("D").ToLowerInvariant(), StringComparison.Ordinal);

    private static string ComputeHash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ClassifyFailureStage(Exception exception)
        => exception is InvalidDataException or FileNotFoundException or DirectoryNotFoundException
            ? "InputValidation"
            : "ReportProcessing";
}
