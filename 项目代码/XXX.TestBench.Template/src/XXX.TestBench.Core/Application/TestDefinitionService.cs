using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.TestDefinitions;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>试验项定义与参数定义管理。参数值由 ParameterDefinition.Validate 统一校验。</summary>
public sealed class TestDefinitionService
{
    private readonly ITestDefinitionRepository _definitions;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public TestDefinitionService(ITestDefinitionRepository definitions, IClock clock, IAuditLog audit)
    {
        _definitions = definitions;
        _clock = clock;
        _audit = audit;
    }

    public async Task<TestItemDefinition> CreateItemAsync(UserContext actor, string code, string name, string executorCode, string resultKind, int sortOrder, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestDefinitions);
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("试验项代码不能为空");
        if (string.IsNullOrWhiteSpace(executorCode)) throw new DomainException("试验项必须指定执行器代码");
        var existing = await _definitions.ListItemsAsync(includeDisabled: true, ct);
        if (existing.Any(i => i.Code == code.Trim()))
            throw new DomainException($"试验项代码 {code} 已存在");
        var item = new TestItemDefinition
        {
            Code = code.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim(),
            ExecutorCode = executorCode.Trim(),
            ResultKind = string.IsNullOrWhiteSpace(resultKind) ? "PassFail" : resultKind.Trim(),
            SortOrder = sortOrder,
            CreatedAtUtc = _clock.UtcNow
        };
        await _definitions.AddItemAsync(item, ct);
        await _audit.WriteAsync(actor.LoginName, "TestItemCreated", $"item:{item.Id}", code, ct);
        return item;
    }

    public async Task SetItemEnabledAsync(UserContext actor, int itemId, bool enabled, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestDefinitions);
        var item = await _definitions.GetItemAsync(itemId, ct) ?? throw new DomainException("试验项定义不存在");
        item.IsEnabled = enabled;
        await _definitions.UpdateItemAsync(item, ct);
        await _audit.WriteAsync(actor.LoginName, enabled ? "TestItemEnabled" : "TestItemDisabled", $"item:{itemId}", null, ct);
    }

    public async Task<ParameterDefinition> CreateParameterAsync(UserContext actor, int itemId, string code, string name, ParameterDataType dataType, bool isRequired,
        string? unit, decimal? minValue, decimal? maxValue, int? precision, IReadOnlyList<string>? allowedValues, int sortOrder, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestDefinitions);
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("参数代码不能为空");
        var item = await _definitions.GetItemAsync(itemId, ct) ?? throw new DomainException("试验项定义不存在");
        var existing = await _definitions.ListParametersAsync(itemId, ct);
        if (existing.Any(p => p.Code == code.Trim()))
            throw new DomainException($"试验项 {item.Code} 下参数代码 {code} 已存在");
        if (dataType == ParameterDataType.Enum && (allowedValues is null || allowedValues.Count == 0))
            throw new DomainException("枚举参数必须提供可选值");
        if (minValue.HasValue && maxValue.HasValue && minValue > maxValue)
            throw new DomainException("参数下限不能大于上限");

        var parameter = new ParameterDefinition
        {
            TestItemDefinitionId = itemId,
            Code = code.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim(),
            DataType = dataType,
            Unit = unit,
            IsRequired = isRequired,
            MinValue = minValue,
            MaxValue = maxValue,
            Precision = precision,
            AllowedValues = allowedValues ?? Array.Empty<string>(),
            SortOrder = sortOrder
        };
        await _definitions.AddParameterAsync(parameter, ct);
        await _audit.WriteAsync(actor.LoginName, "ParameterCreated", $"param:{parameter.Id}", $"{item.Code}.{code}", ct);
        return parameter;
    }

    public async Task UpdateParameterAsync(UserContext actor, int parameterId, string name, bool isRequired, decimal? minValue, decimal? maxValue, string? unit, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageTestDefinitions);
        var parameter = await _definitions.GetParameterAsync(parameterId, ct) ?? throw new DomainException("参数定义不存在");
        if (minValue.HasValue && maxValue.HasValue && minValue > maxValue)
            throw new DomainException("参数下限不能大于上限");
        parameter.Name = string.IsNullOrWhiteSpace(name) ? parameter.Name : name.Trim();
        parameter.IsRequired = isRequired;
        parameter.MinValue = minValue;
        parameter.MaxValue = maxValue;
        parameter.Unit = unit;
        await _definitions.UpdateParameterAsync(parameter, ct);
        await _audit.WriteAsync(actor.LoginName, "ParameterUpdated", $"param:{parameterId}", null, ct);
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
