using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.TestParameters;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class TestParameterServiceTests
{
    private static (TestParameterService Service, FakeTestParameterRepository Repository, FakeProductRepository Products, FakeAuditLog Audit) Create()
    {
        var clock = new FixedClock();
        var products = new FakeProductRepository();
        var repository = new FakeTestParameterRepository();
        var audit = new FakeAuditLog();
        var service = new TestParameterService(repository, products, clock, audit);

        var type = new Domain.Products.ProductType { Id = 1, Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new Domain.Products.ProductModel { Id = 1, ProductTypeId = type.Id, Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();
        return (service, repository, products, audit);
    }

    private static UserContext Admin() => TestContexts.With(PermissionCode.ManageTestDefinitions);

    [Fact]
    public async Task LoadEffective_MergesProjectAndProductParameters()
    {
        var (service, repository, _, _) = Create();
        var actor = Admin();
        await service.SaveProjectAsync(actor, 60);
        await service.SaveProductAsync(actor, 1, 1, 5000, 100);

        var effective = await service.LoadEffectiveAsync(1);

        Assert.Equal(1, effective.ProductTypeId);
        Assert.Equal(1, effective.ProductModelId);
        Assert.Equal(60, effective.TestTimeSeconds);
        Assert.Equal(5000, effective.TestVoltageV);
        Assert.Equal(100, effective.ProtectCurrentMa);
        Assert.Single(repository.ProductParameters);
        Assert.NotNull(repository.Project);
    }

    [Fact]
    public async Task LoadEffective_RejectsMissingLevel()
    {
        var (service, repository, _, _) = Create();
        var actor = Admin();
        await service.SaveProjectAsync(actor, 60);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.LoadEffectiveAsync(1));
        Assert.Contains("产品参数", ex.Message);
    }

    [Fact]
    public async Task Save_RejectsOutOfRangeValues()
    {
        var (service, _, _, _) = Create();
        var actor = Admin();

        await Assert.ThrowsAsync<DomainException>(() => service.SaveProjectAsync(actor, 0));
        await Assert.ThrowsAsync<DomainException>(() => service.SaveProductAsync(actor, 1, 1, 6000, 100));
        await Assert.ThrowsAsync<DomainException>(() => service.SaveProductAsync(actor, 1, 1, 5000, -1));
        await Assert.ThrowsAsync<DomainException>(() => service.SaveProductAsync(actor, 1, 1, double.NaN, 100));
        await Assert.ThrowsAsync<DomainException>(() => service.SaveProductAsync(actor, 1, 1, 5000, double.PositiveInfinity));
    }

    [Fact]
    public async Task Save_RequiresManageTestDefinitionsPermission()
    {
        var (service, _, _, _) = Create();
        var viewer = TestContexts.With(PermissionCode.ViewRecords);

        await Assert.ThrowsAsync<AuthorizationException>(() => service.SaveProjectAsync(viewer, 60));
    }

    [Fact]
    public async Task SaveProduct_RequiresMatchingProductTypeAndModel()
    {
        var (service, _, products, _) = Create();
        await products.AddTypeAsync(new Domain.Products.ProductType
        {
            Id = 2,
            Name = "绝缘试验",
            CreatedAtUtc = DateTime.UtcNow
        });

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.SaveProductAsync(Admin(), 2, 1, 5000, 100));

        Assert.Contains("不匹配", ex.Message);
    }

    [Fact]
    public void Snapshot_RoundTrip_PreservesValues()
    {
        var value = new EffectiveTestParameters
        {
            ProductTypeId = 3,
            ProductModelId = 7,
            TestTimeSeconds = 90,
            TestVoltageV = 1200.5,
            ProtectCurrentMa = 250
        };

        var json = TestParameterSnapshot.ToJson(value);
        var restored = TestParameterSnapshot.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(value, restored);
        Assert.Null(TestParameterSnapshot.FromJson(null));
        Assert.Null(TestParameterSnapshot.FromJson("not-json"));
    }
}
