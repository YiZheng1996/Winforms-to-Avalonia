using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class ProductServiceTests
{
    private static (ProductService Service, FakeProductRepository Products) Create()
    {
        var clock = new FixedClock();
        var products = new FakeProductRepository();
        return (new ProductService(products, clock, new FakeAuditLog()), products);
    }

    [Fact]
    public async Task CreateType_AndModel_EnforcesUniqueCodes()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);

        var type = await service.CreateTypeAsync(actor, "PT", "压力试验");
        var model = await service.CreateModelAsync(actor, type.Id, "M1", "型号1");

        Assert.Equal("PT", type.Code);
        Assert.Equal(type.Id, model.ProductTypeId);
        await Assert.ThrowsAsync<DomainException>(() => service.CreateTypeAsync(actor, "PT", "重复"));
        await Assert.ThrowsAsync<DomainException>(() => service.CreateModelAsync(actor, type.Id, "M1", "重复型号"));
    }

    [Fact]
    public async Task CreateModel_DisabledType_IsRejected()
    {
        var (service, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "PT", "压力试验");
        await service.SetTypeEnabledAsync(actor, type.Id, false);

        await Assert.ThrowsAsync<DomainException>(() => service.CreateModelAsync(actor, type.Id, "M1", "型号1"));
    }

    [Fact]
    public async Task ManageProductsPermission_IsRequired()
    {
        var (service, _) = Create();
        var viewer = TestContexts.With(PermissionCode.ViewRecords);
        await Assert.ThrowsAsync<AuthorizationException>(() => service.CreateTypeAsync(viewer, "PT", "压力试验"));
    }
}
