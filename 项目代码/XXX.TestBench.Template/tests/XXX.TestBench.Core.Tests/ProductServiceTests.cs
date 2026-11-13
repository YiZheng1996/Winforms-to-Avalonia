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
    public async Task CreateType_AndModel_AssignsIds()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);

        var type = await service.CreateTypeAsync(actor, "压力试验");
        var model = await service.CreateModelAsync(actor, type.Id, "型号1");

        Assert.True(type.Id > 0);
        Assert.Equal(type.Id, model.ProductTypeId);
        Assert.True(model.Id > 0);
    }

    [Fact]
    public async Task CreateModel_DisabledType_IsRejected()
    {
        var (service, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "压力试验");
        await service.SetTypeEnabledAsync(actor, type.Id, false);

        await Assert.ThrowsAsync<DomainException>(() => service.CreateModelAsync(actor, type.Id, "型号1"));
    }

    [Fact]
    public async Task DeleteModel_WithoutRecords_Succeeds()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "压力试验");
        var model = await service.CreateModelAsync(actor, type.Id, "型号1");

        await service.DeleteModelAsync(actor, model.Id);

        Assert.Empty(products.Models);
    }

    [Fact]
    public async Task DeleteModel_WithRecords_IsRejected()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "压力试验");
        var model = await service.CreateModelAsync(actor, type.Id, "型号1");
        products.RecordModelReferences.Add(model.Id);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.DeleteModelAsync(actor, model.Id));

        Assert.Contains("试验记录", error.Message);
        Assert.Single(products.Models);
    }

    [Fact]
    public async Task DeleteType_WithModelsOrPoints_IsRejected()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "压力试验");
        await service.CreateModelAsync(actor, type.Id, "型号1");

        var modelError = await Assert.ThrowsAsync<DomainException>(() => service.DeleteTypeAsync(actor, type.Id));
        Assert.Contains("产品型号", modelError.Message);

        products.Models.Clear();
        products.TestPointTypeReferences.Add(type.Id);
        var pointError = await Assert.ThrowsAsync<DomainException>(() => service.DeleteTypeAsync(actor, type.Id));
        Assert.Contains("试验项点", pointError.Message);
    }

    [Fact]
    public async Task DeleteType_WithoutChildren_Succeeds()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "压力试验");

        await service.DeleteTypeAsync(actor, type.Id);

        Assert.Empty(products.Types);
    }

    [Fact]
    public async Task RenameType_UpdatesName()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "压力试验");

        await service.RenameTypeAsync(actor, type.Id, "绝缘试验");

        var updated = await products.GetTypeAsync(type.Id);
        Assert.NotNull(updated);
        Assert.Equal("绝缘试验", updated!.Name);
    }

    [Fact]
    public async Task RenameType_EmptyName_IsRejected()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "压力试验");

        var error = await Assert.ThrowsAsync<DomainException>(() => service.RenameTypeAsync(actor, type.Id, "   "));

        Assert.Contains("名称", error.Message);
        Assert.Equal("压力试验", (await products.GetTypeAsync(type.Id))!.Name);
    }

    [Fact]
    public async Task RenameType_MissingType_IsRejected()
    {
        var (service, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        await Assert.ThrowsAsync<DomainException>(() => service.RenameTypeAsync(actor, 999, "绝缘试验"));
    }

    [Fact]
    public async Task RenameModel_UpdatesName_AndKeepsType()
    {
        var (service, products) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        var type = await service.CreateTypeAsync(actor, "压力试验");
        var model = await service.CreateModelAsync(actor, type.Id, "型号1");

        await service.RenameModelAsync(actor, model.Id, "型号2");

        var updated = await products.GetModelAsync(model.Id);
        Assert.NotNull(updated);
        Assert.Equal("型号2", updated!.Name);
        Assert.Equal(type.Id, updated.ProductTypeId);
    }

    [Fact]
    public async Task RenameModel_MissingModel_IsRejected()
    {
        var (service, _) = Create();
        var actor = TestContexts.With(PermissionCode.ManageProducts);
        await Assert.ThrowsAsync<DomainException>(() => service.RenameModelAsync(actor, 999, "型号2"));
    }

    [Fact]
    public async Task Rename_RequiresManageProductsPermission()
    {
        var (service, _) = Create();
        var viewer = TestContexts.With(PermissionCode.ViewRecords);
        await Assert.ThrowsAsync<AuthorizationException>(() => service.RenameTypeAsync(viewer, 1, "绝缘试验"));
        await Assert.ThrowsAsync<AuthorizationException>(() => service.RenameModelAsync(viewer, 1, "型号2"));
    }

    [Fact]
    public async Task ManageProductsPermission_IsRequired()
    {
        var (service, _) = Create();
        var viewer = TestContexts.With(PermissionCode.ViewRecords);
        await Assert.ThrowsAsync<AuthorizationException>(() => service.CreateTypeAsync(viewer, "压力试验"));
    }
}
