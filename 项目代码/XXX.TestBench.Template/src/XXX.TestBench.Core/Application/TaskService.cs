using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 本地任务创建与执行闭环：任务只关联产品型号，不依赖配方；启动时固化直编参数快照。
/// </summary>
public sealed class TaskService
{
    private readonly ITaskRepository _tasks;
    private readonly IProductRepository _products;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;
    private readonly IUnitOfWorkFactory? _unitOfWorkFactory;

    public TaskService(ITaskRepository tasks, IProductRepository products, IClock clock, IAuditLog audit, IUnitOfWorkFactory? unitOfWorkFactory = null)
    {
        _tasks = tasks;
        _products = products;
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

    public async Task<TestTask> CreateAsync(UserContext actor, int productModelId, ProductIdentity identity, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTasks);
        var model = await _products.GetModelAsync(productModelId, ct) ?? throw new DomainException("产品型号不存在");
        if (!model.IsEnabled) throw new DomainException("产品型号已停用，不能创建任务");
        if (!identity.HasAnyValue) throw new DomainException("至少填写一项产品标识（产品编号/批次号/工位号/备注）");

        string taskNumber;
        do
        {
            taskNumber = $"T-{_clock.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        } while (await _tasks.ExistsTaskNumberAsync(taskNumber, ct));

        var task = new TestTask
        {
            TaskNumber = taskNumber,
            ProductModelId = productModelId,
            ProductIdentity = identity,
            CreatedByUserId = actor.UserId,
            CreatedAtUtc = _clock.UtcNow
        };
        await _tasks.AddAsync(task, ct);
        await _audit.WriteAsync(actor.LoginName, "TaskCreated", $"task:{task.Id}", taskNumber, ct);
        return task;
    }

    public async Task UpdateIdentityAsync(UserContext actor, int taskId, ProductIdentity identity, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTasks);
        var task = await GetTaskAsync(taskId, ct);
        if (task.State != TaskState.Draft) throw new DomainException("只有草稿任务可以编辑产品标识");
        if (!identity.HasAnyValue) throw new DomainException("至少填写一项产品标识（产品编号/批次号/工位号/备注）");
        task.ProductIdentity = identity;
        await _tasks.UpdateAsync(task, ct);
        await _audit.WriteAsync(actor.LoginName, "TaskIdentityUpdated", $"task:{taskId}", identity.ProductNumber, ct);
    }

    public async Task ToReadyAsync(UserContext actor, int taskId, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        var task = await GetTaskAsync(taskId, ct);
        task.ToReady();
        await _tasks.UpdateAsync(task, ct);
    }

    /// <summary>
    /// 启动试验：固化参数快照与设备模式并创建执行记录。带事务工厂时任务状态与记录原子提交。
    /// </summary>
    public async Task<TestRecord> StartAsync(UserContext actor, int taskId, DeviceMode mode, string parameterSnapshot, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        var task = await GetTaskAsync(taskId, ct);

        if (_unitOfWorkFactory is not null)
        {
            await using var uow = _unitOfWorkFactory.Create();
            await uow.BeginTransactionAsync(ct);
            try
            {
                var record = await StartCoreAsync(actor, task, parameterSnapshot, mode, ct);
                await uow.CommitAsync(ct);
                return record;
            }
            catch
            {
                await uow.RollbackAsync(ct);
                throw;
            }
        }

        return await StartCoreAsync(actor, task, parameterSnapshot, mode, ct);
    }

    private async Task<TestRecord> StartCoreAsync(UserContext actor, TestTask task, string parameterSnapshot, DeviceMode mode, CancellationToken ct)
    {
        task.Start(_clock.UtcNow);
        var record = new TestRecord
        {
            TaskId = task.Id,
            ParameterSnapshot = parameterSnapshot,
            DeviceMode = mode,
            OperatorUserId = actor.UserId,
            StartedAtUtc = _clock.UtcNow
        };
        await _tasks.UpdateAsync(task, ct);
        await _tasks.AddRecordAsync(record, ct);
        await _audit.WriteAsync(actor.LoginName, "TestStarted", $"task:{task.Id}", $"record:{record.Id}", ct);
        return record;
    }

    public async Task CompleteAsync(UserContext actor, int taskId, string conclusion, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        var task = await GetTaskAsync(taskId, ct);
        task.Complete(_clock.UtcNow);
        await _tasks.UpdateAsync(task, ct);
        await _audit.WriteAsync(actor.LoginName, "TaskCompleted", $"task:{taskId}", conclusion, ct);
    }

    public async Task FailAsync(UserContext actor, int taskId, string reason, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        var task = await GetTaskAsync(taskId, ct);
        task.Fail(_clock.UtcNow);
        await _tasks.UpdateAsync(task, ct);
        await _audit.WriteAsync(actor.LoginName, "TaskFailed", $"task:{taskId}", reason, ct);
    }

    public async Task CancelAsync(UserContext actor, int taskId, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTasks);
        var task = await GetTaskAsync(taskId, ct);
        task.Cancel(_clock.UtcNow);
        await _tasks.UpdateAsync(task, ct);
        await _audit.WriteAsync(actor.LoginName, "TaskCancelled", $"task:{taskId}", null, ct);
    }

    private async Task<TestTask> GetTaskAsync(int taskId, CancellationToken ct) =>
        await _tasks.GetAsync(taskId, ct) ?? throw new DomainException("任务不存在");
}
