using System.Security.Cryptography;
using System.Text;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 终态与自动报表意图的持久化结论。Unknown 时调用方必须保留恢复占用并禁止新动作。
/// </summary>
public enum RunFinalizationDisposition
{
    Committed = 0,
    NotCommitted = 1,
    PersistenceOutcomeUnknown = 2
}

/// <summary>
/// 终态保存结果；RequestId 来自 Pending 报表请求并用于提交结果查证。
/// </summary>
public sealed record RunFinalizationResult(
    RunFinalizationDisposition Disposition,
    string RequestId,
    ReportRecord? PendingReport,
    Exception? Failure = null);

/// <summary>
/// 将运行终态和自动 Pending 报表写入同一事务，并通过稳定 RequestId 查证提交结果。
/// </summary>
public sealed class TestRunFinalizationService
{
    private readonly IRecordRepository _records;
    private readonly IReportRepository _reports;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public TestRunFinalizationService(
        IRecordRepository records,
        IReportRepository reports,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
    }

    /// <summary>
    /// 原子保存一次试验终态及对应的自动报表 Pending 意图。
    /// 同一 RequestId 的同一内容调用为幂等；若提交异常则重新读取请求和主记录判定结果。
    /// </summary>
    public async Task<RunFinalizationResult> FinalizeAsync(
        TestRecord terminalRecord,
        ReportRecord pendingReport,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(terminalRecord);
        ArgumentNullException.ThrowIfNull(pendingReport);
        ValidateRequest(terminalRecord, pendingReport);

        var requestId = pendingReport.RequestId!;
        var existing = await _reports.GetByRequestIdAsync(requestId, ct);
        if (existing is not null)
        {
            if (!IsSameRequest(existing, pendingReport))
                throw new DomainException("报表请求标识已用于不同内容，拒绝重复提交。");

            var persisted = await _records.GetRecordAsync(terminalRecord.Id, ct);
            if (!MatchesFinalizedRecord(persisted, terminalRecord, existing))
                throw new DomainException("报表请求已存在，但对应试验终态与本次输入不一致。");

            return new(RunFinalizationDisposition.Committed, requestId, existing);
        }

        var expectedRowVersion = terminalRecord.RowVersion;
        ReportRecord? createdReport = null;
        Exception? commitFailure = null;
        var commitAttempted = false;

        try
        {
            await using (var unitOfWork = _unitOfWorkFactory.Create())
            {
                await unitOfWork.BeginTransactionAsync(ct);
                await _records.UpdateRecordAsync(terminalRecord, ct);
                createdReport = await _reports.CreatePendingAsync(pendingReport, ct);
                commitAttempted = true;
                await unitOfWork.CommitAsync(ct);
            }
        }
        catch (Exception ex) when (commitAttempted)
        {
            // Commit 或其后的释放异常都不能直接解释成回滚；先结束事务上下文，再从新连接查证。
            commitFailure = ex;
        }

        if (commitFailure is null)
            return new(RunFinalizationDisposition.Committed, requestId, createdReport);

        return await VerifyCommitOutcomeAsync(
            terminalRecord,
            pendingReport,
            requestId,
            expectedRowVersion,
            commitFailure);
    }

    private async Task<RunFinalizationResult> VerifyCommitOutcomeAsync(
        TestRecord terminalRecord,
        ReportRecord request,
        string requestId,
        long expectedRowVersion,
        Exception commitFailure)
    {
        try
        {
            // 查证必须越过已结束的事务上下文，并且不能被原取消令牌中断。
            var persistedReport = await _reports.GetByRequestIdAsync(requestId, CancellationToken.None);
            var persistedRecord = await _records.GetRecordAsync(terminalRecord.Id, CancellationToken.None);

            if (persistedReport is not null
                && IsSameRequest(persistedReport, request)
                && MatchesFinalizedRecord(persistedRecord, terminalRecord, persistedReport))
                return new(RunFinalizationDisposition.Committed, requestId, persistedReport, commitFailure);

            if (persistedReport is null
                && persistedRecord is { State: RecordState.Running }
                && persistedRecord.RowVersion == expectedRowVersion
                && string.Equals(persistedRecord.RunId, terminalRecord.RunId, StringComparison.Ordinal)
                && string.Equals(persistedRecord.StartRequestId, terminalRecord.StartRequestId, StringComparison.Ordinal)
                && string.Equals(persistedRecord.OperationId, terminalRecord.OperationId, StringComparison.Ordinal)
                && string.Equals(persistedRecord.BenchId, terminalRecord.BenchId, StringComparison.Ordinal)
                && string.Equals(persistedRecord.NodeId, terminalRecord.NodeId, StringComparison.Ordinal))
            {
                // 仓储在事务内乐观递增了内存对象的 RowVersion；确认回滚后恢复成数据库版本，允许按原请求重试。
                terminalRecord.RowVersion = expectedRowVersion;
                return new(RunFinalizationDisposition.NotCommitted, requestId, null, commitFailure);
            }

            return new(
                RunFinalizationDisposition.PersistenceOutcomeUnknown,
                requestId,
                persistedReport,
                new InvalidDataException("提交异常后，RequestId 与试验记录状态不能共同证明已提交或已回滚。", commitFailure));
        }
        catch (Exception verificationFailure)
        {
            return new(
                RunFinalizationDisposition.PersistenceOutcomeUnknown,
                requestId,
                null,
                new AggregateException("提交异常后的持久化查证失败。", commitFailure, verificationFailure));
        }
    }

    private static void ValidateRequest(TestRecord record, ReportRecord report)
    {
        if (record.Id <= 0 || record.ResultContractVersion != 2 || record.RowVersion < 0
            || record.FinishedAtUtc is null
            || record.State is not (RecordState.Completed or RecordState.Failed or RecordState.Aborted or RecordState.Interrupted))
            throw new DomainException("终态提交需要一条已持久化的新版试验记录和结束时间。");

        var expectedEvaluation = record.State == RecordState.Completed
            ? EvaluationState.Pending
            : EvaluationState.NotEvaluated;
        var expectedPurpose = record.State == RecordState.Completed
            ? ReportPurpose.ProductEvaluation
            : ReportPurpose.ProcessOnly;
        var serializedInput = record.ReportInputJson;
        var actualHash = serializedInput is null
            ? null
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serializedInput))).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(record.RunId)
            || string.IsNullOrWhiteSpace(record.NodeId)
            || record.EvaluationState != expectedEvaluation
            || serializedInput is null
            || !string.Equals(record.InputHash, actualHash, StringComparison.Ordinal)
            || report.TestRecordId != record.Id
            || !string.Equals(report.InputHash, record.InputHash, StringComparison.Ordinal)
            || report.Purpose != expectedPurpose
            || !string.Equals(report.Trigger, "Automatic", StringComparison.Ordinal)
            || !string.Equals(report.OwnerNodeId, record.NodeId, StringComparison.Ordinal)
            || report.CreatedByUserId != record.OperatorUserId
            || report.Status != ReportStatus.Pending
            || string.IsNullOrWhiteSpace(report.RequestId))
            throw new DomainException("试验终态、冻结报表输入与自动报表任务合同不匹配。");
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
           && existing.Purpose == request.Purpose
           && existing.CreatedByUserId == request.CreatedByUserId;

    private static bool MatchesFinalizedRecord(TestRecord? persisted, TestRecord request, ReportRecord report)
        => persisted is not null
           && persisted.Id == request.Id
           && persisted.ResultContractVersion == 2
           && persisted.State == request.State
           && IsAllowedEvaluationProgress(persisted.EvaluationState, request.State, report.Purpose)
           && string.Equals(persisted.RunId, request.RunId, StringComparison.Ordinal)
           && string.Equals(persisted.StartRequestId, request.StartRequestId, StringComparison.Ordinal)
           && string.Equals(persisted.OperationId, request.OperationId, StringComparison.Ordinal)
           && string.Equals(persisted.BenchId, request.BenchId, StringComparison.Ordinal)
           && string.Equals(persisted.NodeId, request.NodeId, StringComparison.Ordinal)
           && string.Equals(persisted.ReportInputJson, request.ReportInputJson, StringComparison.Ordinal)
           && string.Equals(persisted.InputHash, request.InputHash, StringComparison.Ordinal)
           && string.Equals(persisted.Conclusion, request.Conclusion, StringComparison.Ordinal)
           && string.Equals(persisted.ExecutionError, request.ExecutionError, StringComparison.Ordinal)
           && persisted.FinishedAtUtc == request.FinishedAtUtc;

    private static bool IsAllowedEvaluationProgress(
        EvaluationState? persisted,
        RecordState state,
        ReportPurpose? purpose)
        => purpose switch
        {
            ReportPurpose.ProductEvaluation when state == RecordState.Completed
                => persisted is EvaluationState.Pending or EvaluationState.Qualified or EvaluationState.Unqualified or EvaluationState.Error,
            ReportPurpose.ProcessOnly when state is RecordState.Failed or RecordState.Aborted or RecordState.Interrupted
                => persisted is EvaluationState.NotEvaluated or EvaluationState.Error,
            _ => false
        };
}
