using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.TestDefinitions;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class TestDefinitionServiceTests
{
    private static (TestDefinitionService Service, FakeTestDefinitionRepository Definitions) Create()
    {
        var clock = new FixedClock();
        var definitions = new FakeTestDefinitionRepository();
        return (new TestDefinitionService(definitions, clock, new FakeAuditLog()), definitions);
    }

    [Fact]
    public async Task CreateItem_AndParameter_WithValidation()
    {
        var (service, definitions) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestDefinitions);

        var item = await service.CreateItemAsync(actor, "IT1", "耐压", "PressureExecutor", "PassFail", 1);
        var parameter = await service.CreateParameterAsync(actor, item.Id, "Voltage", "试验电压", ParameterDataType.Decimal, isRequired: true,
            unit: "kV", minValue: 0, maxValue: 50, precision: 2, allowedValues: null, sortOrder: 1);

        Assert.Equal("IT1", item.Code);
        Assert.Equal(item.Id, parameter.TestItemDefinitionId);
        Assert.Equal(ParameterDataType.Decimal, parameter.DataType);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateItemAsync(actor, "IT1", "重复", "X", "PassFail", 2));
    }

    [Fact]
    public async Task EnumParameter_RequiresAllowedValues()
    {
        var (service, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestDefinitions);
        var item = await service.CreateItemAsync(actor, "IT1", "耐压", "PressureExecutor", "PassFail", 1);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateParameterAsync(actor, item.Id, "Mode", "模式", ParameterDataType.Enum, true, null, null, null, null, null, 1));
    }

    [Fact]
    public async Task UpdateParameter_AppliesRange()
    {
        var (service, definitions) = Create();
        var actor = TestContexts.With(PermissionCode.ManageTestDefinitions);
        var item = await service.CreateItemAsync(actor, "IT1", "耐压", "PressureExecutor", "PassFail", 1);
        var parameter = await service.CreateParameterAsync(actor, item.Id, "Voltage", "试验电压", ParameterDataType.Decimal, true, "kV", 0, 50, 2, null, 1);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.UpdateParameterAsync(actor, parameter.Id, "试验电压", true, 60, 10, "kV"));

        await service.UpdateParameterAsync(actor, parameter.Id, "试验电压", true, 0, 100, "kV");
        Assert.Equal(100m, parameter.MaxValue);
    }

    [Fact]
    public async Task ManageTestDefinitionsPermission_IsRequired()
    {
        var (service, _) = Create();
        var operatorActor = TestContexts.With(PermissionCode.ExecuteTests);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            service.CreateItemAsync(operatorActor, "IT1", "耐压", "X", "PassFail", 1));
    }
}
