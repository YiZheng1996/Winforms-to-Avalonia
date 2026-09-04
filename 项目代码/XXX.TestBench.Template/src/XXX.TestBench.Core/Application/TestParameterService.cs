using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 直编试验参数用例服务：保存三级参数，并把三级参数合并成自动试验使用的强类型参数。
/// 编辑权限复用 ManageTestDefinitions；所有写入写审计。
/// </summary>
public sealed class TestParameterService
{
    /// <summary>
    /// 参数仓库。
    /// </summary>
    private readonly ITestParameterRepository _parameters;
    /// <summary>
    /// 产品数据仓库。
    /// </summary>
    private readonly IProductRepository _products;
    /// <summary>
    /// 时间来源。
    /// </summary>
    private readonly IClock _clock;
    /// <summary>
    /// 审计日志。
    /// </summary>
    private readonly IAuditLog _audit;

    /// <summary>
    /// 创建试验参数服务。
    /// </summary>
    public TestParameterService(ITestParameterRepository parameters, IProductRepository products, IClock clock, IAuditLog audit)
    {
        _parameters = parameters;
        _products = products;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>
    /// 按产品型号合并三级参数并校验；任一层缺失或超范围直接抛错，禁止启动试验。
    /// </summary>
    public async Task<EffectiveTestParameters> LoadEffectiveAsync(int productModelId, CancellationToken ct = default)
    {
        if (productModelId <= 0) throw new DomainException("未选择有效的产品型号。");
        var model = await _products.GetModelAsync(productModelId, ct) ?? throw new DomainException("产品型号不存在");
        var type = await _products.GetTypeAsync(model.ProductTypeId, ct) ?? throw new DomainException("产品类型不存在");

        var project = await _parameters.GetProjectAsync(ct) ?? throw new DomainException("参数未完整配置：缺少项目参数（试验时间）。");
        var typeParam = await _parameters.GetTypeAsync(type.Id, ct) ?? throw new DomainException("参数未完整配置：缺少产品类型参数（试验电压）。");
        var modelParam = await _parameters.GetModelAsync(productModelId, ct) ?? throw new DomainException("参数未完整配置：缺少产品型号参数（保护电流）。");

        var value = new EffectiveTestParameters
        {
            ProductTypeId = type.Id,
            ProductModelId = model.Id,
            TestTimeSeconds = project.TestTimeSeconds,
            TestVoltageV = typeParam.TestVoltageV,
            ProtectCurrentMa = modelParam.ProtectCurrentMa
        };
        var result = TestParameterValidator.Validate(value);
        if (!result.IsValid) throw new DomainException(result.Error);
        return value;
    }

    /// <summary>
    /// 保存项目级参数（试验时间）。
    /// </summary>
    public async Task SaveProjectAsync(UserContext actor, int testTimeSeconds, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestDefinitions);
        var error = TestParameterValidator.ValidateTestTime(testTimeSeconds);
        if (error is not null) throw new DomainException(error);
        await _parameters.SaveProjectAsync(new ProjectTestParameter
        {
            TestTimeSeconds = testTimeSeconds,
            UpdatedBy = actor.LoginName,
            UpdatedAtUtc = _clock.UtcNow
        }, ct);
        await _audit.WriteAsync(actor.LoginName, "ProjectTestParameterSaved", "project", testTimeSeconds.ToString(), ct);
    }

    /// <summary>
    /// 保存产品类型级参数（试验电压）。
    /// </summary>
    public async Task SaveTypeAsync(UserContext actor, int productTypeId, double testVoltageV, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestDefinitions);
        var type = await _products.GetTypeAsync(productTypeId, ct) ?? throw new DomainException("产品类型不存在");
        var error = TestParameterValidator.ValidateTestVoltage(testVoltageV);
        if (error is not null) throw new DomainException(error);
        await _parameters.SaveTypeAsync(new ProductTypeTestParameter
        {
            ProductTypeId = type.Id,
            TestVoltageV = testVoltageV,
            UpdatedBy = actor.LoginName,
            UpdatedAtUtc = _clock.UtcNow
        }, ct);
        await _audit.WriteAsync(actor.LoginName, "TypeTestParameterSaved", $"type:{type.Id}", testVoltageV.ToString(), ct);
    }

    /// <summary>
    /// 保存产品型号级参数（保护电流）。
    /// </summary>
    public async Task SaveModelAsync(UserContext actor, int productModelId, double protectCurrentMa, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestDefinitions);
        var model = await _products.GetModelAsync(productModelId, ct) ?? throw new DomainException("产品型号不存在");
        var error = TestParameterValidator.ValidateProtectCurrent(protectCurrentMa);
        if (error is not null) throw new DomainException(error);
        await _parameters.SaveModelAsync(new ProductModelTestParameter
        {
            ProductModelId = model.Id,
            ProtectCurrentMa = protectCurrentMa,
            UpdatedBy = actor.LoginName,
            UpdatedAtUtc = _clock.UtcNow
        }, ct);
        await _audit.WriteAsync(actor.LoginName, "ModelTestParameterSaved", $"model:{model.Id}", protectCurrentMa.ToString(), ct);
    }

    /// <summary>
    /// 校验参数编辑权限，越权时写入审计并抛出异常。
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

