using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 试验执行闭环：预检 → 启动记录 → 逐项执行（执行器扩展点）→ 判定汇总 → 完成/失败。
/// 项点结果只从已保存记录与配方快照生成，不修改配方与主数据。
/// </summary>
public sealed class TestExecutionService
{
    private readonly ITaskRepository _tasks;
    private readonly IRecipeRepository _recipes;
    private readonly ITestDefinitionRepository _definitions;
    private readonly ITestItemExecutorFactory _executors;
    private readonly TaskService _taskService;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public TestExecutionService(ITaskRepository tasks, IRecipeRepository recipes, ITestDefinitionRepository definitions,
        ITestItemExecutorFactory executors, TaskService taskService, IClock clock, IAuditLog audit)
    {
        _tasks = tasks;
        _recipes = recipes;
        _definitions = definitions;
        _executors = executors;
        _taskService = taskService;
        _clock = clock;
        _audit = audit;
    }

    public async Task<TestRecord> StartAsync(UserContext actor, int taskId, DeviceMode mode, IDeviceRuntime runtime, CancellationToken ct = default)
    {
        actor.EnsurePermission(PermissionCode.ExecuteTests);
        // 预检：设备模式与连接状态
        if (runtime.Status.Health is not (DeviceHealth.Healthy or DeviceHealth.Degraded) || !runtime.Status.IsConnected)
            throw new DomainException($"设备预检未通过：{runtime.Name} 状态 {runtime.Status.Health}");
        var record = await _taskService.StartAsync(actor, taskId, mode, ct);
        await _audit.WriteAsync(actor.LoginName, "TestPrecheckPassed", $"task:{taskId}", runtime.Name, ct);
        return record;
    }

    public async Task<TestItemResult> ExecuteItemAsync(UserContext actor, int recordId, int recipeItemId, IDeviceRuntime runtime, CancellationToken ct = default)
    {
        actor.EnsurePermission(PermissionCode.ExecuteTests);
        var record = await _tasks.GetRecordAsync(recordId, ct) ?? throw new DomainException("试验记录不存在");
        if (record.State != RecordState.Running) throw new DomainException($"记录状态 {record.State}，不能执行项点");

        var recipeItem = (await _recipes.ListItemsAsync(record.RecipeVersionId, ct)).FirstOrDefault(i => i.Id == recipeItemId)
            ?? throw new DomainException("记录配方中不存在该项点");
        var definition = await _definitions.GetItemAsync(recipeItem.TestItemDefinitionId, ct)
            ?? throw new DomainException("试验项定义不存在");

        var parameterValues = new Dictionary<string, string>();
        var parameterDefs = await _definitions.ListParametersAsync(definition.Id, ct);
        var storedValues = await _recipes.ListParameterValuesAsync(record.RecipeVersionId, ct);
        foreach (var pd in parameterDefs)
        {
            var value = storedValues.FirstOrDefault(v => v.RecipeItemId == recipeItem.Id && v.ParameterDefinitionId == pd.Id);
            if (value is not null) parameterValues[pd.Code] = value.RawValue;
        }

        var executor = _executors.Get(definition.ExecutorCode);
        var result = new TestItemResult { RecordId = recordId, RecipeItemId = recipeItem.Id, TestItemDefinitionId = definition.Id };
        result.Start(_clock.UtcNow);
        var outcome = await executor.ExecuteAsync(new ItemExecutionContext(
            actor, record, new RecipeItemContext(recipeItem.Id, recipeItem.SortOrder, recipeItem.IsEnabled),
            definition, parameterValues, record.DeviceMode, runtime), ct);
        result.SetResult(outcome.State, outcome.SummaryValue, outcome.ResultText, _clock.UtcNow);
        await _tasks.AddItemResultAsync(result, ct);
        await _audit.WriteAsync(actor.LoginName, "ItemExecuted", $"record:{recordId}", $"{definition.Code}:{outcome.State}", ct);
        return result;
    }

    public async Task<string> CompleteAsync(UserContext actor, int recordId, CancellationToken ct = default)
    {
        actor.EnsurePermission(PermissionCode.ExecuteTests);
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
