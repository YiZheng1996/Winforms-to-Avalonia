using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 产品类型与产品型号主数据管理。类型/型号停用后不可新建试验记录；有业务引用的数据只能停用，不能删除。
/// </summary>
public sealed class ProductService
{
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
    /// 创建产品主数据服务。
    /// </summary>
    public ProductService(IProductRepository products, IClock clock, IAuditLog audit)
    {
        _products = products;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>
    /// 新增产品类型。
    /// </summary>
    public async Task<ProductType> CreateTypeAsync(UserContext actor, string name, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("产品类型名称不能为空");
        var type = new ProductType { Name = name.Trim(), CreatedAtUtc = _clock.UtcNow };
        await _products.AddTypeAsync(type, ct);
        await _audit.WriteAsync(actor.LoginName, "ProductTypeCreated", $"type:{type.Id}", type.Name, ct);
        return type;
    }

    /// <summary>
    /// 启用或停用产品类型。
    /// </summary>
    public async Task SetTypeEnabledAsync(UserContext actor, int typeId, bool enabled, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        var type = await _products.GetTypeAsync(typeId, ct) ?? throw new DomainException("产品类型不存在");
        type.IsEnabled = enabled;
        await _products.UpdateTypeAsync(type, ct);
        await _audit.WriteAsync(actor.LoginName, enabled ? "ProductTypeEnabled" : "ProductTypeDisabled", $"type:{typeId}", null, ct);
    }

    /// <summary>
    /// 修改产品类型名称；停用状态与业务引用保持不变。
    /// </summary>
    public async Task RenameTypeAsync(UserContext actor, int typeId, string name, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("产品类型名称不能为空");
        var type = await _products.GetTypeAsync(typeId, ct) ?? throw new DomainException("产品类型不存在");
        var oldName = type.Name;
        type.Name = name.Trim();
        await _products.UpdateTypeAsync(type, ct);
        await _audit.WriteAsync(actor.LoginName, "ProductTypeRenamed", $"type:{typeId}", $"{oldName} -> {type.Name}", ct);
    }

    /// <summary>
    /// 在产品类型下新增产品型号。
    /// </summary>
    public async Task<ProductModel> CreateModelAsync(UserContext actor, int productTypeId, string name, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("产品型号名称不能为空");
        var type = await _products.GetTypeAsync(productTypeId, ct) ?? throw new DomainException("产品类型不存在");
        if (!type.IsEnabled) throw new DomainException("产品类型已停用，不能新增型号");
        var model = new ProductModel { ProductTypeId = productTypeId, Name = name.Trim(), CreatedAtUtc = _clock.UtcNow };
        await _products.AddModelAsync(model, ct);
        await _audit.WriteAsync(actor.LoginName, "ProductModelCreated", $"model:{model.Id}", model.Name, ct);
        return model;
    }

    /// <summary>
    /// 启用或停用产品型号。
    /// </summary>
    public async Task SetModelEnabledAsync(UserContext actor, int modelId, bool enabled, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        var model = await _products.GetModelAsync(modelId, ct) ?? throw new DomainException("产品型号不存在");
        model.IsEnabled = enabled;
        await _products.UpdateModelAsync(model, ct);
        await _audit.WriteAsync(actor.LoginName, enabled ? "ProductModelEnabled" : "ProductModelDisabled", $"model:{modelId}", null, ct);
    }

    /// <summary>
    /// 修改产品型号名称；所属产品类型与启用状态保持不变。
    /// </summary>
    public async Task RenameModelAsync(UserContext actor, int modelId, string name, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("产品型号名称不能为空");
        var model = await _products.GetModelAsync(modelId, ct) ?? throw new DomainException("产品型号不存在");
        var oldName = model.Name;
        model.Name = name.Trim();
        await _products.UpdateModelAsync(model, ct);
        await _audit.WriteAsync(actor.LoginName, "ProductModelRenamed", $"model:{modelId}", $"{oldName} -> {model.Name}", ct);
    }

    /// <summary>
    /// 删除没有下属型号和试验项点的产品类型。
    /// </summary>
    public async Task DeleteTypeAsync(UserContext actor, int typeId, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        var type = await _products.GetTypeAsync(typeId, ct) ?? throw new DomainException("产品类型不存在");
        if (await _products.CountModelsByTypeAsync(typeId, ct) > 0)
            throw new DomainException("该产品类型下还有产品型号，请先删除产品型号后再删除");
        if (await _products.CountTestPointsByTypeAsync(typeId, ct) > 0)
            throw new DomainException("该产品类型下还有试验项点，请先删除试验项点后再删除");

        await _products.DeleteTypeAsync(typeId, ct);
        await _audit.WriteAsync(actor.LoginName, "ProductTypeDeleted", $"type:{typeId}", type.Name, ct);
    }

    /// <summary>
    /// 删除没有试验记录的产品型号；型号级参数与项点配置随型号一并清理。
    /// </summary>
    public async Task DeleteModelAsync(UserContext actor, int modelId, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageProducts);
        var model = await _products.GetModelAsync(modelId, ct) ?? throw new DomainException("产品型号不存在");
        if (await _products.CountRecordsByModelAsync(modelId, ct) > 0)
            throw new DomainException("该产品型号已有试验记录，不能删除（可停用）");

        await _products.DeleteModelAsync(modelId, ct);
        await _audit.WriteAsync(actor.LoginName, "ProductModelDeleted", $"model:{modelId}", model.Name, ct);
    }

    /// <summary>
    /// 校验管理权限，越权时写入审计并抛出异常。
    /// </summary>
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
