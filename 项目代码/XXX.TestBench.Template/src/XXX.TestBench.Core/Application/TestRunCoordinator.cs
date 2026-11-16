using System.Text.Json;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Execution;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 新版纯仿真执行的唯一任务所有者。准备、开始与配置共用短期互斥门，
/// 执行期间由持久 fence 和内存占用保护；提交不明不重发动作、不自动续测。
/// 当前只允许只读仿真执行器，真实设备许可与项目安全收尾需独立适配。
/// </summary>
public sealed class TestRunCoordinator
{
    private readonly DeviceOperationCoordinator _operations;
    private readonly TestRunPreparationService _preparation;
    private readonly TestRunStartPersistenceService _start;
    private readonly TestRunFinalizationService _finalization;
    private readonly IRecordRepository _records;
    private readonly IOperationEvidenceStore _evidence;
    private readonly ITestItemExecutorFactory _executors;
    private readonly Func<IDeviceRuntime?> _runtime;
    private readonly SimulationRunOptions _options;
    private readonly IClock _clock;
    private readonly TimeProvider _time;
    private TestRunPreparationSession? _ready;
    private StartRequest? _acceptedRequest;
    private string? _acceptedSessionToken;
    private RunHandle? _handle;
    private CancellationTokenSource? _stop;
    private Task _completion = Task.CompletedTask;
    private OperationFence? _fence;
    private volatile RunSnapshot _snapshot;
    private long _startedTimestamp;

    public TestRunCoordinator(DeviceOperationCoordinator operations, TestRunPreparationService preparation,
        TestRunStartPersistenceService start, TestRunFinalizationService finalization,
        IRecordRepository records, IOperationEvidenceStore evidence, ITestItemExecutorFactory executors,
        Func<IDeviceRuntime?> runtime, SimulationRunOptions options, IClock clock, TimeProvider time)
    {
        _operations = operations;
        _preparation = preparation;
        _start = start;
        _finalization = finalization;
        _records = records;
        _evidence = evidence;
        _executors = executors;
        _runtime = runtime;
        _options = options;
        _clock = clock;
        _time = time;
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BenchId);
        if (!Guid.TryParseExact(options.DatabaseInstanceId, "D", out var id) || id == Guid.Empty)
            throw new ArgumentException("数据库身份无效。", nameof(options));
        _snapshot = new(null, null, null, options.BenchId, BenchPhase.Idle, [], null, null,
            0, 0, null, null, TimeSpan.Zero, "尚未准备", false, false);
    }

    public RunSnapshot Snapshot => _snapshot;
    /// <summary>供关闭流程和离线验证等待；执行异常以 RecoveryRequired 状态保留。</summary>
    public Task Completion => _completion;

    public async Task<TestRunPreparationSession> PrepareAsync(string sessionToken, int productModelId,
        ProductIdentity identity, RunScope scope, int? selectedPointId = null, CancellationToken ct = default)
    {
        await using var gate = await _operations.EnterExecutionAsync(ct);
        if (_snapshot.RunState != BenchPhase.Idle)
            throw new DomainException("请先结束或取消当前操作。");
        var fences = await _evidence.ReadAllAsync(ct);
        var active = await _records.GetActiveRunningRecordAsync(_options.BenchId, ct);
        if (fences.Count > 0 || active is not null)
        {
            var operationId = fences.FirstOrDefault()?.OperationId
                ?? (Guid.TryParse(active?.OperationId, out var persistedId) ? persistedId : Guid.NewGuid());
            _operations.Reserve(operationId);
            _snapshot = _snapshot with { OperationId = operationId, RunState = BenchPhase.RecoveryRequired,
                RecoveryReasons = [RecoveryReasonCode.InterruptedOperation], StatusMessage = "检测到未闭合操作，禁止自动续测。" };
            throw new DomainException(_snapshot.StatusMessage);
        }
        var ready = await _preparation.PrepareAsync(sessionToken, productModelId, identity, scope, selectedPointId, ct);
        if (ready.BenchId != _options.BenchId) throw new DomainException("准备会话台架身份不匹配。");
        _operations.Reserve(ready.OperationId);
        _ready = ready;
        _acceptedRequest = null;
        _handle = null;
        _snapshot = new(ready.OperationId, null, ready.PreparationSessionId, ready.BenchId, BenchPhase.Ready,
            [], ready.Scope, null, 0, ready.Sequence.Count, null, null, TimeSpan.Zero, "仿真准备完成", true, true);
        return ready;
    }

    public async Task<RunHandle> StartAsync(StartRequest request, string sessionToken, CancellationToken ct = default)
    {
        await using var gate = await _operations.EnterExecutionAsync(ct);
        // 重复点击只返回同一后台任务的句柄，不再经过“无活动记录”的准备检查。
        if (_acceptedRequest == request && _handle is not null && _acceptedSessionToken == sessionToken)
            return _handle;
        if (_snapshot.RunState != BenchPhase.Ready || _ready?.PreparationSessionId != request.PreparationSessionId)
            throw new DomainException("没有匹配的可开始准备会话。");
        var prepared = await _preparation.ValidateStartAsync(request, sessionToken, ct);
        var runtime = _runtime() ?? throw new DomainException("仿真运行时不存在。");
        var executors = prepared.Preparation.Sequence.Select(item => _executors.Get(item.ExecutorCode)).ToArray();
        var signals = await new SignalResolver(runtime, prepared.ResolvedSignalPointIds, () => _clock.UtcNow)
            .ResolveAsync(executors.SelectMany(executor => executor.RequiredSignals), ct);
        var access = new SimulationRunDeviceAccess(runtime, _runtime, signals, _ready.DeviceConfigurationRevision,
            prepared.Preparation.VersionVectorJson, _clock);
        var record = CreateRecord(prepared);
        // 在发生任何事务前校验资源输入合同，避免到终态才发现缺少模板摘要。
        CreateReportInput(record, prepared.Preparation, Array.Empty<TestItemResult>(), validateOnly: true);
        _fence = new(OperationFence.CurrentSchemaVersion, 0, request.OperationId, _ready.BenchId,
            _ready.NodeId, _options.DatabaseInstanceId, prepared.RunId, _ready.DeviceConfigurationRevision,
            BenchPhase.Running, false, null, null, false, null, null, [], _clock.UtcNow.ToString("O"));
        try
        {
            await _evidence.WriteAsync(_fence, null, ct);
            var persisted = await _start.CreateAsync(record, _ready.Sequence, ct);
            if (persisted.Disposition != RunStartPersistenceDisposition.Committed || !persisted.CreatedInThisCall)
                throw new DomainException("开始事务未证明由本次新建提交；保留恢复占用，不重复启动执行器。");
            _acceptedRequest = request;
            _acceptedSessionToken = sessionToken;
            _handle = new(request.OperationId, prepared.RunId, record.Id);
            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            _startedTimestamp = _time.GetTimestamp();
            _snapshot = _snapshot with { RunId = prepared.RunId, RunState = BenchPhase.Running,
                StartedAtUtc = record.StartedAtUtc, CanStart = false, CanStop = true, StatusMessage = "仿真执行中" };
            var token = _stop.Token;
            _completion = Task.Run(() => ExecuteAsync(prepared, record, persisted.ItemResults, executors, access, token));
            return _handle;
        }
        catch (Exception ex)
        {
            access.Revoke();
            await SetRecoveryAsync(ex);
            throw;
        }
    }

    /// <summary>准备阶段取消无需收尾；开始后只通知唯一任务，终态提交开始后不改写结果。</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        Task completion;
        CancellationTokenSource? cancel = null;
        await using (var gate = await _operations.EnterExecutionAsync(ct))
        {
            if (_snapshot.RunState == BenchPhase.Ready && _ready is not null)
            {
                _operations.Release(_ready.OperationId);
                _ready = null;
                _snapshot = _snapshot with { RunState = BenchPhase.Idle, CanStart = false, CanStop = false, StatusMessage = "准备已取消" };
                return;
            }
            if (_snapshot.RunState == BenchPhase.Running)
            {
                _snapshot = _snapshot with { RunState = BenchPhase.Stopping, CanStop = false, StatusMessage = "正在停止仿真" };
                cancel = _stop;
            }
            completion = _completion;
        }
        // 取消回调可能执行用户代码，必须放在设备门外。
        try { cancel?.Cancel(); }
        catch (ObjectDisposedException) when (completion.IsCompleted) { /* 所属任务已完成，新运行不会复用此取消源。 */ }
        await completion.WaitAsync(ct);
    }

    private async Task ExecuteAsync(PreparedTestRun prepared, TestRecord record,
        IReadOnlyList<TestItemResult> slots, IReadOnlyList<ITestItemExecutor> executors,
        SimulationRunDeviceAccess access, CancellationToken token)
    {
        var terminal = RecordState.Completed;
        string? error = null;
        TestItemResult? current = null;
        var persistencePending = false;
        try
        {
            for (var index = 0; index < prepared.Preparation.Sequence.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                access.EnsureValid();
                var item = prepared.Preparation.Sequence[index];
                var executor = executors[index];
                if (executor.OutputContractVersion != item.OutputContractVersion)
                    throw new DomainException("执行器输出合同与冻结项点不一致。");
                current = slots.Single(slot => slot.TestItemPointId == item.PointId);
                current.Start(_clock.UtcNow);
                persistencePending = true;
                await _records.UpdateItemResultAsync(current, token);
                persistencePending = false;
                await using (var progressGate = await _operations.EnterExecutionAsync())
                    _snapshot = _snapshot with { CurrentPointId = item.PointId, CurrentStep = item.Name };
                var outcome = await executor.ExecuteAsync(new(prepared.Actor, record,
                    new(item.PointId, item.SortOrder, true), item, DeviceMode.Simulation, access, prepared.Preparation.Parameters), token);
                token.ThrowIfCancellationRequested();
                access.EnsureValid();
                ValidateMeasurements(executor, outcome);
                current.SetExecutionResult(outcome.State, outcome.SummaryValue, outcome.ResultText, outcome.Measurements ?? [], _clock.UtcNow);
                persistencePending = true;
                await _records.UpdateItemResultAsync(current, token);
                persistencePending = false;
                current = null;
                await using (var progressGate = await _operations.EnterExecutionAsync())
                    _snapshot = _snapshot with { CompletedPointCount = index + 1, Elapsed = _time.GetElapsedTime(_startedTimestamp) };
                if (outcome.State != ItemResultState.Succeeded)
                {
                    terminal = outcome.State == ItemResultState.Aborted ? RecordState.Aborted : RecordState.Failed;
                    error = outcome.ResultText;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            terminal = RecordState.Aborted;
            error = "操作员停止仿真试验。";
        }
        catch (Exception ex)
        {
            terminal = RecordState.Failed;
            error = ex.Message;
        }
        finally
        {
            // 本许可从未允许写入，撤销后不会有迟到设备动作；不把此确认用于硬件收尾。
            access.Revoke();
        }

        try
        {
            if (persistencePending)
                throw new DomainException("项点保存结果未确认，禁止重试执行或覆盖结果：" + error);
            await using (var gate = await _operations.EnterExecutionAsync())
            {
                if (_snapshot.RunState == BenchPhase.Stopping && terminal == RecordState.Completed)
                {
                    terminal = RecordState.Aborted;
                    error = "操作员在终态冻结前停止仿真试验。";
                }
                _snapshot = _snapshot with { RunState = BenchPhase.Finalizing, CanStop = false, StatusMessage = "正在保存仿真结果" };
            }
            if (current is { State: ItemResultState.Running })
            {
                current.SetExecutionResult(terminal == RecordState.Aborted ? ItemResultState.Aborted : ItemResultState.Failed,
                    null, error, [], _clock.UtcNow);
                await _records.UpdateItemResultAsync(current);
            }
            // 不重试失败的项点写入。任何不确定槽位都留给恢复查证，不能据内存伪造完整终态。
            var stored = await _records.ListItemResultsAsync(record.Id);
            if (stored.Count != slots.Count || stored.Any(slot => slot.State == ItemResultState.Running)
                || (terminal == RecordState.Completed && stored.Any(slot => slot.State != ItemResultState.Succeeded)))
                throw new DomainException("项点落库状态不能证明执行结果，需恢复查证。");
            record.State = terminal;
            record.FinishedAtUtc = _clock.UtcNow;
            record.ExecutionError = error;
            record.EvaluationState = terminal == RecordState.Completed ? EvaluationState.Pending : EvaluationState.NotEvaluated;
            var input = CreateReportInput(record, prepared.Preparation, stored);
            record.ReportInputJson = input.JsonText;
            record.InputHash = input.InputHash;
            var next = _fence! with { Revision = _fence!.Revision + 1, Phase = BenchPhase.Finalizing,
                SafeStopConfirmed = true, LastSafeStopJson = "{\"mode\":\"Simulation\",\"writesAllowed\":false,\"permitRevoked\":true}",
                LastEvidenceKey = _fence.OperationId.ToString("D"), UpdatedAtUtc = _clock.UtcNow.ToString("O") };
            await _evidence.WriteAsync(next, _fence.Revision);
            _fence = next;
            var pending = new ReportRecord { TestRecordId = record.Id, TemplatePath = _options.Resources.TemplateKey,
                TemplateHash = _options.Resources.TemplateHash, MappingHash = _options.Resources.MappingHash,
                RequestId = StableIdentity.FromSeed("automatic-report-request:" + record.RunId),
                RevisionId = StableIdentity.FromSeed("automatic-report-revision:" + record.RunId),
                Purpose = terminal == RecordState.Completed ? ReportPurpose.ProductEvaluation : ReportPurpose.ProcessOnly,
                Trigger = "Automatic", OwnerNodeId = record.NodeId, CreatedByUserId = record.OperatorUserId,
                CreatedAtUtc = _clock.UtcNow, InputHash = input.InputHash };
            var finalized = await _finalization.FinalizeAsync(record, pending);
            if (finalized.Disposition != RunFinalizationDisposition.Committed)
                throw new DomainException("试验终态与报表任务提交未确认，保留恢复占用。");
            await _evidence.ClearAfterSafeStopAsync(next.OperationId, next.BenchId, next.Revision);
            await using var release = await _operations.EnterExecutionAsync();
            _operations.Release(next.OperationId);
            _snapshot = _snapshot with { RunState = BenchPhase.Idle, CanStart = false, CanStop = false,
                Elapsed = _time.GetElapsedTime(_startedTimestamp), StatusMessage = $"仿真试验已保存：{terminal}；报表待生成" };
        }
        catch (Exception ex)
        {
            await using var gate = await _operations.EnterExecutionAsync();
            await SetRecoveryAsync(ex);
        }
    }

    private async Task SetRecoveryAsync(Exception failure)
    {
        _snapshot = _snapshot with { RunState = BenchPhase.RecoveryRequired, CanStart = false, CanStop = false,
            RecoveryReasons = [RecoveryReasonCode.PersistencePending], StatusMessage = failure.Message };
        if (_fence is null) return;
        try
        {
            var actual = await _evidence.ReadAsync(_fence.OperationId);
            if (actual is null) return; // 首次写盘失败仍保留内存占用；数据库还未执行。
            var next = actual with { Revision = actual.Revision + 1, Phase = BenchPhase.RecoveryRequired,
                RecoveryReasons = [RecoveryReasonCode.PersistencePending], UpdatedAtUtc = _clock.UtcNow.ToString("O") };
            await _evidence.WriteAsync(next, actual.Revision);
            _fence = next;
        }
        catch (Exception evidenceFailure)
        {
            _snapshot = _snapshot with { StatusMessage = failure.Message + "；恢复标记写入失败：" + evidenceFailure.Message };
        }
    }

    private TestRecord CreateRecord(PreparedTestRun prepared) => new()
    {
        RecordNumber = prepared.RunId.ToString("D"), RunId = prepared.RunId.ToString("D"),
        StartRequestId = prepared.StartRequestId.ToString("D"), OperationId = prepared.Preparation.OperationId.ToString("D"),
        BenchId = prepared.Preparation.BenchId, NodeId = prepared.Preparation.NodeId,
        ProductModelId = prepared.Preparation.ProductModelId, ProductIdentity = prepared.Preparation.ProductIdentity,
        ModelNameSnapshot = prepared.Preparation.ProductModelName, OperatorNameSnapshot = prepared.Actor.DisplayName,
        OperatorUserId = prepared.Actor.UserId, Scope = prepared.Preparation.Scope, DeviceMode = DeviceMode.Simulation,
        ParameterSnapshot = prepared.Preparation.ParameterSnapshotJson, SequenceSnapshot = prepared.Preparation.SequenceSnapshotJson,
        DeviceConfigurationRevision = prepared.Preparation.DeviceConfigurationRevision,
        SignalBindingsSnapshot = prepared.Preparation.SignalBindingsSnapshotJson,
        ResourceSnapshotJson = JsonSerializer.Serialize(_options.Resources), PrecheckSnapshotJson = prepared.Preparation.VersionVectorJson,
        StartedAtUtc = _clock.UtcNow
    };

    private SerializedReportInput CreateReportInput(TestRecord record, TestRunPreparationSession session,
        IReadOnlyList<TestItemResult> slots, bool validateOnly = false)
    {
        var state = validateOnly ? RecordState.Aborted : record.State;
        return ReportInputSnapshotSerializer.Serialize(new()
        {
            DatabaseInstanceId = _options.DatabaseInstanceId, RunId = record.RunId!, ResultContractVersion = 2,
            Purpose = state == RecordState.Completed ? ReportPurpose.ProductEvaluation : ReportPurpose.ProcessOnly,
            Scope = session.Scope, ExecutionState = state, ExecutionError = record.ExecutionError,
            Metadata = new() { RecordNumber = record.RecordNumber, RunId = record.RunId!,
                ProductNumber = record.ProductIdentity.ProductNumber, VehicleNumber = record.ProductIdentity.VehicleNumber,
                BatchNumber = record.ProductIdentity.BatchNumber, StationNumber = record.ProductIdentity.StationNumber,
                Remark = record.ProductIdentity.Remark, ProductModelName = session.ProductModelName,
                OperatorName = record.OperatorNameSnapshot!, StartedAtUtc = record.StartedAtUtc,
                FinishedAtUtc = validateOnly ? record.StartedAtUtc : record.FinishedAtUtc,
                DeviceMode = DeviceMode.Simulation, Scope = session.Scope, ExecutionState = state },
            Parameters = [new() { Field = "TestVoltageV", Kind = MeasuredValueKind.Number, Number = session.Parameters.TestVoltageV, Unit = "V" },
                new() { Field = "ProtectCurrentMa", Kind = MeasuredValueKind.Number, Number = session.Parameters.ProtectCurrentMa, Unit = "mA" },
                new() { Field = "TestTimeSeconds", Kind = MeasuredValueKind.Number, Number = session.Parameters.TestTimeSeconds, Unit = "s" }],
            Items = session.Sequence.Select(item => new ReportInputTestItem { PointId = item.PointId, Name = item.Name,
                ExecutorCode = item.ExecutorCode, OutputContractVersion = item.OutputContractVersion,
                State = slots.SingleOrDefault(slot => slot.TestItemPointId == item.PointId)?.State ?? ItemResultState.Pending,
                Measurements = slots.SingleOrDefault(slot => slot.TestItemPointId == item.PointId)?.Measurements ?? [] }).ToArray(),
            DeviceModes = JsonSerializer.Deserialize<VersionDevices>(session.VersionVectorJson)?.DeviceSessions
                .Select(device => new ReportInputDeviceMode(device.DeviceId, DeviceMode.Simulation)).ToArray() ?? [],
            Resources = _options.Resources, TargetTimeZoneId = _options.TargetTimeZoneId
        });
    }

    private static void ValidateMeasurements(ITestItemExecutor executor, ItemExecutionOutcome outcome)
    {
        var values = outcome.Measurements ?? [];
        foreach (var value in values)
        {
            value.EnsureValid();
            var definition = executor.Outputs.SingleOrDefault(output => output.Key == value.Key);
            if (definition is null || definition.Kind != value.Kind || definition.Unit != value.Unit)
                throw new DomainException("执行器测量值不符合声明的输出合同。");
        }
        if (outcome.State == ItemResultState.Succeeded && executor.Outputs.Any(output => output.Required
            && !values.Any(value => value.Key == output.Key && value.Valid)))
            throw new DomainException("执行完成但缺少必需的有效测量值。");
    }

    private sealed record VersionDevices(IReadOnlyList<VersionDevice> DeviceSessions);
    private sealed record VersionDevice(string DeviceId);
}
