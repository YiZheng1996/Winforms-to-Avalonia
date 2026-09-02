using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 产品类型与产品型号主数据管理。类型/型号停用后不可新建任务（TaskService 校验）。
/// </summary>
public sealed class ProductService
{
    private readonly IProductRepository _products;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public ProductService(IProductRepository products, IClock clock, IAuditLog audit)
    {
        _products = products;
        _clock = clock;
        _audit = audit;
    }

    public async Task<ProductType> CreateTypeAsync(UserContext actor, string code, string name, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("产品类型代码不能为空");
        if (await _products.GetTypeByCodeAsync(code, ct) is not null)
            throw new DomainException($"产品类型代码 {code} 已存在");
        var type = new ProductType { Code = code.Trim(), Name = string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim(), CreatedAtUtc = _clock.UtcNow };
        await _products.AddTypeAsync(type, ct);
        await _audit.WriteAsync(actor.LoginName, "ProductTypeCreated", $"type:{type.Id}", code, ct);
        return type;
    }

    public async Task SetTypeEnabledAsync(UserContext actor, int typeId, bool enabled, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        var type = await _products.GetTypeAsync(typeId, ct) ?? throw new DomainException("产品类型不存在");
        type.IsEnabled = enabled;
        await _products.UpdateTypeAsync(type, ct);
        await _audit.WriteAsync(actor.LoginName, enabled ? "ProductTypeEnabled" : "ProductTypeDisabled", $"type:{typeId}", null, ct);
    }

    public async Task<ProductModel> CreateModelAsync(UserContext actor, int productTypeId, string code, string name, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("产品型号代码不能为空");
        var type = await _products.GetTypeAsync(productTypeId, ct) ?? throw new DomainException("产品类型不存在");
        if (!type.IsEnabled) throw new DomainException("产品类型已停用，不能新增型号");
        if (await _products.GetModelByCodeAsync(productTypeId, code, ct) is not null)
            throw new DomainException($"产品类型 {type.Code} 下型号代码 {code} 已存在");
        var model = new ProductModel { ProductTypeId = productTypeId, Code = code.Trim(), Name = string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim(), CreatedAtUtc = _clock.UtcNow };
        await _products.AddModelAsync(model, ct);
        await _audit.WriteAsync(actor.LoginName, "ProductModelCreated", $"model:{model.Id}", code, ct);
        return model;
    }

    public async Task SetModelEnabledAsync(UserContext actor, int modelId, bool enabled, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        var model = await _products.GetModelAsync(modelId, ct) ?? throw new DomainException("产品型号不存在");
        model.IsEnabled = enabled;
        await _products.UpdateModelAsync(model, ct);
        await _audit.WriteAsync(actor.LoginName, enabled ? "ProductModelEnabled" : "ProductModelDisabled", $"model:{modelId}", null, ct);
    }

    private void Ensure(UserContext actor, Core.Domain.Identity.PermissionCode permission)
    {
        try { actor.EnsurePermission(permission); }
        catch (AuthorizationException)
        {
            _ = _audit.WriteAsync(actor.LoginName, "AccessDenied", permission.ToString(), null);
            throw;
        }
    }
}
