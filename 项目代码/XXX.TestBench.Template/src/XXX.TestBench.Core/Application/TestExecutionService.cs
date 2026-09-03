using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 试验执行闭环：预检 → 固化参数快照并启动记录 → 按代码固定序列逐项执行（执行器扩展点）→ 判定汇总 → 完成/失败。
/// 项点结果只从已保存记录与参数快照生成，不读取配方或界面控件。
/// </summary>
public sealed class TestExecutionService
{
    private readonly ITaskRepository _tasks;
    private readonly ITestDefinitionRepository _definitions;
    private readonly ITestItemExecutorFactory _executors;
    private readonly TaskService _taskService;
    private readonly TestParameterService _testParameters;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;
    private readonly IUnitOfWorkFactory? _unitOfWorkFactory;

    public TestExecutionService(ITaskRepository tasks, ITestDefinitionRepository definitions, ITestItemExecutorFactory executors,
        TaskService taskService, TestParameterService testParameters, IClock clock, IAuditLog audit, IUnitOfWorkFactory? unitOfWorkFactory = null)
    {
        _tasks = tasks;
        _definitions = definitions;
        _executors = executors;
        _taskService = taskService;
        _testParameters = testParameters;
        _clock = clock;
        _audit = audit;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    private void Ensure(UserContext actor, PermissionCode permission)
    {
        try { actor.EnsurePermission(permission); }
        catch (AuthorizationException)
        {
            _ = _audit.WriteAsync(actor.LoginName, "AccessDenied", permission.ToString(), null);
            throw;
        }
    }

    /// <summary>
    /// 启动试验：先设备预检，再合并并校验三级直编参数，最后固化快照并启动记录。
    /// 参数缺失或超范围时任务保持就绪，不产生记录。
    /// </summary>
    public async Task<TestRecord> StartAsync(UserContext actor, int taskId, DeviceMode mode, IDeviceRuntime runtime, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        if (runtime.Status.Health is not (DeviceHealth.Healthy or DeviceHealth.Degraded) || !runtime.Status.IsConnected)
            throw new DomainException($"设备预检未通过：{runtime.Name} 状态 {runtime.Status.Health}");

        var task = await _tasks.GetAsync(taskId, ct) ?? throw new DomainException("任务不存在");
        var effective = await _testParameters.LoadEffectiveAsync(task.ProductModelId, ct);
        var snapshot = TestParameterSnapshot.ToJson(effective);

        var record = await _taskService.StartAsync(actor, taskId, mode, snapshot, ct);
        await _audit.WriteAsync(actor.LoginName, "TestPrecheckPassed", $"task:{taskId}", runtime.Name, ct);
        return record;
    }

    /// <summary>
    /// 返回代码固定的执行序列（按序号升序）。
    /// </summary>
    public async Task<IReadOnlyList<TestItemDefinition>> GetSequenceAsync(CancellationToken ct = default)
    {
        var items = await _definitions.ListItemsAsync(includeDisabled: false, ct);
        return items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
    }

    /// <summary>
    /// 执行固定序列中的一项试验，参数只取记录内固化的快照。
    /// </summary>
    public async Task<TestItemResult> ExecuteItemAsync(UserContext actor, int recordId, int itemDefinitionId, IDeviceRuntime runtime, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        var record = await _tasks.GetRecordAsync(recordId, ct) ?? throw new DomainException("试验记录不存在");
        if (record.State != RecordState.Running) throw new DomainException($"记录状态 {record.State}，不能执行项点");

        var definition = await _definitions.GetItemAsync(itemDefinitionId, ct)
            ?? throw new DomainException("试验项定义不存在");
        if (!definition.IsEnabled) throw new DomainException("试验项已停用，不能执行");

        var effective = TestParameterSnapshot.FromJson(record.ParameterSnapshot)
            ?? throw new DomainException("试验参数快照缺失，不能执行项点");
        var executor = _executors.Get(definition.ExecutorCode);

        var result = new TestItemResult { RecordId = recordId, TestItemDefinitionId = definition.Id };
        result.Start(_clock.UtcNow);
        var outcome = await executor.ExecuteAsync(new ItemExecutionContext(
            actor, record, new SequenceItemContext(definition.Id, definition.SortOrder, definition.IsEnabled),
            definition, new Dictionary<string, string>(), record.DeviceMode, runtime, effective), ct);
        result.SetResult(outcome.State, outcome.SummaryValue, outcome.ResultText, _clock.UtcNow);
        await _tasks.AddItemResultAsync(result, ct);
        await _audit.WriteAsync(actor.LoginName, "ItemExecuted", $"record:{recordId}", $"{definition.Code}:{outcome.State}", ct);
        return result;
    }

    public async Task<string> CompleteAsync(UserContext actor, int recordId, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        if (_unitOfWorkFactory is not null)
        {
            await using var uow = _unitOfWorkFactory.Create();
            await uow.BeginTransactionAsync(ct);
            try
            {
                var conclusion = await CompleteCoreAsync(actor, recordId, ct);
                await uow.CommitAsync(ct);
                return conclusion;
            }
            catch
            {
                await uow.RollbackAsync(ct);
                throw;
            }
        }
        return await CompleteCoreAsync(actor, recordId, ct);
    }

    private async Task<string> CompleteCoreAsync(UserContext actor, int recordId, CancellationToken ct)
    {
        var record = await _tasks.GetRecordAsync(recordId, ct) ?? throw new DomainException("试验记录不存在");
        var results = await _tasks.ListItemResultsAsync(recordId, ct);
        if (results.Count == 0 || results.Any(r => r.State is ItemResultState.Pending or ItemResultState.Running))
            throw new DomainException("还有项点未执行完成，不能结束试验");

        var failedCount = results.Count(r => r.State == ItemResultState.Failed);
        var conclusion = failedCount == 0
            ? $"全部通过（{results.Count}/{results.Count}）"
            : $"存在失败项点（{failedCount}/{results.Count}）";

        if (failedCount == 0)
        {
            record.Complete(_clock.UtcNow, conclusion);
            await _tasks.UpdateRecordAsync(record, ct);
            await _taskService.CompleteAsync(actor, record.TaskId, conclusion, ct);
        }
        else
        {
            record.Fail(_clock.UtcNow, conclusion);
            await _tasks.UpdateRecordAsync(record, ct);
            await _taskService.FailAsync(actor, record.TaskId, conclusion, ct);
        }
        await _audit.WriteAsync(actor.LoginName, "TestFinished", $"record:{recordId}", conclusion, ct);
        return conclusion;
    }
}
