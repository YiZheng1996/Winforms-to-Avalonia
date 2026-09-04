using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 试验执行闭环：设备预检 → 校验三级直编参数并固化参数快照 → 按型号的项点配置固化序列快照
/// 并创建试验记录 → 逐项执行（编译期执行器扩展点）→ 判定汇总 → 完成/失败。
/// 项点结果只从已保存记录与快照生成，不读取界面控件。
/// </summary>
public sealed class TestExecutionService
{
    /// <summary>
    /// 试验记录仓库。
    /// </summary>
    private readonly IRecordRepository _records;
    /// <summary>
    /// 产品数据仓库。
    /// </summary>
    private readonly IProductRepository _products;
    /// <summary>
    /// 执行器工厂。
    /// </summary>
    private readonly ITestItemExecutorFactory _executors;
    /// <summary>
    /// 试验项点服务。
    /// </summary>
    private readonly TestPointService _testPoints;
    /// <summary>
    /// 试验参数服务。
    /// </summary>
    private readonly TestParameterService _testParameters;
    /// <summary>
    /// 时间来源。
    /// </summary>
    private readonly IClock _clock;
    /// <summary>
    /// 审计日志。
    /// </summary>
    private readonly IAuditLog _audit;
    /// <summary>
    /// 工作单元工厂，用于结束时保证事务性。
    /// </summary>
    private readonly IUnitOfWorkFactory? _unitOfWorkFactory;

    /// <summary>
    /// 创建试验执行服务。
    /// </summary>
    public TestExecutionService(IRecordRepository records, IProductRepository products, ITestItemExecutorFactory executors,
        TestPointService testPoints, TestParameterService testParameters, IClock clock, IAuditLog audit, IUnitOfWorkFactory? unitOfWorkFactory = null)
    {
        _records = records;
        _products = products;
        _executors = executors;
        _testPoints = testPoints;
        _testParameters = testParameters;
        _clock = clock;
        _audit = audit;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    /// <summary>
    /// 校验试验执行权限，越权时写入审计并抛出异常。
    /// </summary>
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
    /// 启动试验：设备预检 → 校验产品型号与项点配置 → 合并并校验三级直编参数 →
    /// 固化参数快照与项点序列快照并直接创建试验记录（不存在任务概念）。
    /// </summary>
    public async Task<TestRecord> StartAsync(UserContext actor, int productModelId, ProductIdentity identity,
        DeviceMode mode, IDeviceRuntime runtime, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        if (runtime.Status.Health is not (DeviceHealth.Healthy or DeviceHealth.Degraded) || !runtime.Status.IsConnected)
            throw new DomainException($"设备预检未通过：{runtime.Name} 状态 {runtime.Status.Health}");
        if (!identity.HasAnyValue) throw new DomainException("至少填写一项产品标识（产品编号/批次号/工位号/备注）");

        var model = await _products.GetModelAsync(productModelId, ct) ?? throw new DomainException("产品型号不存在");
        if (!model.IsEnabled) throw new DomainException("产品型号已停用，不能开始试验");

        var sequence = await _testPoints.GetSequenceAsync(productModelId, ct);
        if (sequence.Count == 0) throw new DomainException("该产品型号尚未配置试验项点，请先在参数管理中完成项点配置");

        var effective = await _testParameters.LoadEffectiveAsync(productModelId, ct);
        var parameterJson = TestParameterSnapshot.ToJson(effective);
        var sequenceJson = SequenceSnapshot.ToJson(sequence
            .Select(p => new SequenceItem(p.Id, p.Name, p.ExecutorCode, p.ResultKind, p.SortOrder))
            .ToList());

        string recordNumber;
        do
        {
            recordNumber = $"R-{_clock.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        } while (await _records.ExistsRecordNumberAsync(recordNumber, ct));

        var record = new TestRecord
        {
            RecordNumber = recordNumber,
            ProductModelId = model.Id,
            ProductIdentity = identity,
            ParameterSnapshot = parameterJson,
            SequenceSnapshot = sequenceJson,
            DeviceMode = mode,
            OperatorUserId = actor.UserId,
            StartedAtUtc = _clock.UtcNow
        };
        await _records.AddRecordAsync(record, ct);
        await _audit.WriteAsync(actor.LoginName, "TestStarted", $"record:{record.Id}", $"model:{model.Id} {record.RecordNumber}", ct);
        return record;
    }

    /// <summary>
    /// 返回记录内固化的项点序列快照（按序号升序）。
    /// </summary>
    public async Task<IReadOnlyList<SequenceItem>> GetSequenceAsync(int recordId, CancellationToken ct = default)
    {
        var record = await _records.GetRecordAsync(recordId, ct) ?? throw new DomainException("试验记录不存在");
        return SequenceSnapshot.FromJson(record.SequenceSnapshot)
            ?? throw new DomainException("试验记录缺少项点序列快照");
    }

    /// <summary>
    /// 执行记录序列中的一项试验，参数只取记录内固化的快照。
    /// </summary>
    public async Task<TestItemResult> ExecuteItemAsync(UserContext actor, int recordId, int pointId, IDeviceRuntime runtime, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ExecuteTests);
        var record = await _records.GetRecordAsync(recordId, ct) ?? throw new DomainException("试验记录不存在");
        if (record.State != RecordState.Running) throw new DomainException($"记录状态 {record.State}，不能执行项点");

        var sequence = await GetSequenceAsync(recordId, ct);
        var item = sequence.FirstOrDefault(i => i.PointId == pointId)
            ?? throw new DomainException("项点不在记录序列中");

        var effective = TestParameterSnapshot.FromJson(record.ParameterSnapshot)
            ?? throw new DomainException("试验参数快照缺失，不能执行项点");
        var executor = _executors.Get(item.ExecutorCode);

        var result = new TestItemResult { RecordId = recordId, TestItemPointId = item.PointId };
        result.Start(_clock.UtcNow);
        var outcome = await executor.ExecuteAsync(new ItemExecutionContext(
            actor, record, new SequenceItemContext(item.PointId, item.SortOrder, true),
            item, new Dictionary<string, string>(), record.DeviceMode, runtime, effective), ct);
        result.SetResult(outcome.State, outcome.SummaryValue, outcome.ResultText, _clock.UtcNow);
        await _records.AddItemResultAsync(result, ct);
        await _audit.WriteAsync(actor.LoginName, "ItemExecuted", $"record:{recordId}", $"point:{item.PointId} {outcome.State}", ct);
        return result;
    }

    /// <summary>
    /// 完成试验：全部项点执行完毕后汇总判定。
    /// </summary>
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

    /// <summary>
    /// 汇总全部项点结果并完成或失败记录。
    /// </summary>
    private async Task<string> CompleteCoreAsync(UserContext actor, int recordId, CancellationToken ct)
    {
        var record = await _records.GetRecordAsync(recordId, ct) ?? throw new DomainException("试验记录不存在");
        var results = await _records.ListItemResultsAsync(recordId, ct);
        if (results.Count == 0 || results.Any(r => r.State is ItemResultState.Pending or ItemResultState.Running))
            throw new DomainException("还有项点未执行完成，不能结束试验");

        var failedCount = results.Count(r => r.State == ItemResultState.Failed);
        var conclusion = failedCount == 0
            ? $"全部通过（{results.Count}/{results.Count}）"
            : $"存在失败项点（{failedCount}/{results.Count}）";

        if (failedCount == 0)
            record.Complete(_clock.UtcNow, conclusion);
        else
            record.Fail(_clock.UtcNow, conclusion);
        await _records.UpdateRecordAsync(record, ct);
        await _audit.WriteAsync(actor.LoginName, "TestFinished", $"record:{recordId}", conclusion, ct);
        return conclusion;
    }
}
