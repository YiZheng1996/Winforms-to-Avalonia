using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 试验项点与项点配置用例服务。
/// 项点归属于产品类型：项点名称 + 关联逻辑类（编译期注册的 ITestItemExecutor 代码）+ 启用 + 排序。
/// 项点配置把类型下已启用的项点按顺序编排为产品型号的自动试验序列。
/// </summary>
public sealed class TestPointService
{
    /// <summary>
    /// 试验项点仓库。
    /// </summary>
    private readonly ITestPointRepository _points;
    /// <summary>
    /// 型号项点配置仓库。
    /// </summary>
    private readonly IModelPointConfigRepository _configs;
    /// <summary>
    /// 产品数据仓库。
    /// </summary>
    private readonly IProductRepository _products;
    /// <summary>
    /// 执行器工厂。
    /// </summary>
    private readonly ITestItemExecutorFactory _executors;
    /// <summary>
    /// 时间来源。
    /// </summary>
    private readonly IClock _clock;
    /// <summary>
    /// 审计日志。
    /// </summary>
    private readonly IAuditLog _audit;

    /// <summary>
    /// 创建试验项点服务。
    /// </summary>
    public TestPointService(ITestPointRepository points, IModelPointConfigRepository configs, IProductRepository products,
        ITestItemExecutorFactory executors, IClock clock, IAuditLog audit)
    {
        _points = points;
        _configs = configs;
        _products = products;
        _executors = executors;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>
    /// 已注册的关联逻辑类（执行器）代码，供项点管理界面选择。
    /// </summary>
    public IReadOnlyCollection<string> ExecutorCodes => _executors.Codes;

    /// <summary>
    /// 按产品类型列出试验项点（读取不校验权限，页面级已受控）。
    /// </summary>
    public Task<IReadOnlyList<TestItemPoint>> ListPointsAsync(int productTypeId, bool includeDisabled, CancellationToken ct = default)
        => _points.ListByTypeAsync(productTypeId, includeDisabled, ct);

    /// <summary>
    /// 新增试验项点。
    /// </summary>
    public async Task<TestItemPoint> CreatePointAsync(UserContext actor, int productTypeId, string name,
        string executorCode, string resultKind, int sortOrder, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestPoints);
        name = name.Trim();
        executorCode = executorCode.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("项点名称不能为空");
        if (string.IsNullOrWhiteSpace(executorCode)) throw new DomainException("请选择关联逻辑类");
        if (!_executors.Codes.Contains(executorCode)) throw new DomainException("未注册的执行器：" + executorCode);
        var type = await _products.GetTypeAsync(productTypeId, ct) ?? throw new DomainException("产品类型不存在");
        if (!type.IsEnabled) throw new DomainException("产品类型已停用，不能新增项点");
        var now = _clock.UtcNow;
        var point = new TestItemPoint
        {
            ProductTypeId = type.Id,
            Name = name,
            ExecutorCode = executorCode,
            ResultKind = string.IsNullOrWhiteSpace(resultKind) ? "PassFail" : resultKind.Trim(),
            IsEnabled = true,
            SortOrder = sortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await _points.AddAsync(point, ct);
        await _audit.WriteAsync(actor.LoginName, "TestPointCreated", $"type:{type.Id}", $"point:{point.Id} {point.Name}", ct);
        return point;
    }

    /// <summary>
    /// 修改试验项点（名称、关联逻辑类、判定类型、启用、排序）。
    /// </summary>
    public async Task UpdatePointAsync(UserContext actor, int pointId, string name, string executorCode,
        string resultKind, bool isEnabled, int sortOrder, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestPoints);
        var point = await _points.GetAsync(pointId, ct) ?? throw new DomainException("试验项点不存在");
        name = name.Trim();
        executorCode = executorCode.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("项点名称不能为空");
        if (string.IsNullOrWhiteSpace(executorCode)) throw new DomainException("请选择关联逻辑类");
        if (!_executors.Codes.Contains(executorCode)) throw new DomainException("未注册的执行器：" + executorCode);

        point.Name = name;
        point.ExecutorCode = executorCode;
        point.ResultKind = string.IsNullOrWhiteSpace(resultKind) ? "PassFail" : resultKind.Trim();
        point.IsEnabled = isEnabled;
        point.SortOrder = sortOrder;
        point.UpdatedAtUtc = _clock.UtcNow;
        await _points.UpdateAsync(point, ct);
        await _audit.WriteAsync(actor.LoginName, "TestPointUpdated", $"point:{pointId}", point.Name, ct);
    }

    /// <summary>
    /// 删除试验项点；已被型号配置或试验记录引用的项点禁止删除。
    /// </summary>
    public async Task DeletePointAsync(UserContext actor, int pointId, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestPoints);
        var point = await _points.GetAsync(pointId, ct) ?? throw new DomainException("试验项点不存在");
        var modelRefs = await _points.CountModelReferencesAsync(pointId, ct);
        if (modelRefs > 0) throw new DomainException("该项点已被产品型号配置引用，请先在项点配置中移除后再删除");
        var resultRefs = await _points.CountResultReferencesAsync(pointId, ct);
        if (resultRefs > 0) throw new DomainException("该试验项点已有执行结果，不能删除（可停用）");
        await _points.DeleteAsync(pointId, ct);
        await _audit.WriteAsync(actor.LoginName, "TestPointDeleted", $"point:{pointId}", point.Name, ct);
    }

    /// <summary>
    /// 读取型号已配置的项点（按顺序，仅启用项点），用于试验执行。
    /// </summary>
    public async Task<IReadOnlyList<TestItemPoint>> GetSequenceAsync(int productModelId, CancellationToken ct = default)
    {
        var model = await _products.GetModelAsync(productModelId, ct) ?? throw new DomainException("产品型号不存在");
        var configs = await _configs.ListByModelAsync(productModelId, ct);
        var points = await _points.ListByTypeAsync(model.ProductTypeId, includeDisabled: true, ct);
        return configs.OrderBy(c => c.SortOrder)
            .Join(points, c => c.TestItemPointId, p => p.Id, (c, p) => p)
            .Where(p => p.IsEnabled)
            .ToList();
    }

    /// <summary>
    /// 读取型号已配置的项点（含停用项点），用于项点配置页展示。
    /// </summary>
    public async Task<IReadOnlyList<TestItemPoint>> ListConfiguredPointsAsync(int productModelId, CancellationToken ct = default)
    {
        var model = await _products.GetModelAsync(productModelId, ct) ?? throw new DomainException("产品型号不存在");
        var configs = await _configs.ListByModelAsync(productModelId, ct);
        var points = await _points.ListByTypeAsync(model.ProductTypeId, includeDisabled: true, ct);
        return configs.OrderBy(c => c.SortOrder)
            .Join(points, c => c.TestItemPointId, p => p.Id, (c, p) => p)
            .ToList();
    }

    /// <summary>
    /// 整体替换型号的项点配置：按传入顺序编排为 1..n 的执行序列（事务内完成）。
    /// </summary>
    public async Task SaveConfigurationAsync(UserContext actor, int productModelId, IReadOnlyList<int> orderedPointIds, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestPoints);
        var model = await _products.GetModelAsync(productModelId, ct) ?? throw new DomainException("产品型号不存在");
        if (!model.IsEnabled) throw new DomainException("产品型号已停用，不能修改项点配置");
        var type = await _products.GetTypeAsync(model.ProductTypeId, ct) ?? throw new DomainException("产品类型不存在");
        if (!type.IsEnabled) throw new DomainException("产品类型已停用，不能修改项点配置");

        var points = await _points.ListByTypeAsync(type.Id, includeDisabled: true, ct);
        if (orderedPointIds.Distinct().Count() != orderedPointIds.Count) throw new DomainException("项点配置存在重复项点");
        var configs = new List<ModelPointConfig>();
        var order = 1;
        foreach (var pointId in orderedPointIds)
        {
            var point = points.FirstOrDefault(p => p.Id == pointId) ?? throw new DomainException("项点不存在或不属于该产品类型");
            if (!point.IsEnabled) throw new DomainException($"项点“{point.Name}”已停用，不能加入配置");
            configs.Add(new ModelPointConfig(productModelId, pointId, order++));
        }
        await _configs.ReplaceAsync(productModelId, configs, ct);
        await _audit.WriteAsync(actor.LoginName, "ModelPointConfigSaved", $"model:{productModelId}", string.Join(",", orderedPointIds), ct);
    }

    /// <summary>
    /// 校验试验项点管理权限，越权时写入审计并抛出异常。
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
}
