using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.App.Tests;

/// <summary>
/// 产品类型/型号在界面与查询结果中的显示规则测试。
/// </summary>
public sealed class ProductModelDisplayTests
{
    [Fact]
    public void SelectionOptionDisplayText_UsesNamesInsteadOfIds()
    {
        var option = new ProductModelSelectionOption(
            ProductTypeId: 11,
            ProductTypeName: "压力试验",
            ProductModelId: 27,
            ProductModelName: "M-200",
            IsEnabled: true,
            CreatedAtUtc: DateTime.UtcNow);

        Assert.Equal("压力试验", option.TypeDisplay);
        Assert.Equal("M-200", option.ModelDisplay);
    }

    [Fact]
    public void ShellProductInfo_UsesModelName_AndDoesNotUseModelIdAsProductNumber()
    {
        var shell = new ShellViewModel(null, isFaulted: false, faultMessage: null);
        shell.SelectProductModel(new ProductModelSelectionOption(
            ProductTypeId: 11,
            ProductTypeName: "压力试验",
            ProductModelId: 27,
            ProductModelName: "M-200",
            IsEnabled: true,
            CreatedAtUtc: DateTime.UtcNow));

        Assert.Equal("M-200", shell.CurrentProductModelNameText);
        Assert.Equal("M-200", shell.CurrentProductModelText);
        Assert.Equal("未录入", shell.CurrentProductNumberText);
    }

    [Fact]
    public async Task DataReports_ResolvesModelNameFromProductModelId()
    {
        using var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        var type = await harness.Services.Products.CreateTypeAsync(actor, "压力试验");
        var model = await harness.Services.Products.CreateModelAsync(actor, type.Id, "M-200");

        await harness.Services.RecordRepository.AddRecordAsync(new TestRecord
        {
            RecordNumber = "R-20260905-0001",
            ProductModelId = model.Id,
            ProductIdentity = new ProductIdentity("P-001", null, null, null),
            DeviceMode = DeviceMode.Simulation,
            OperatorUserId = actor.UserId,
            StartedAtUtc = DateTime.UtcNow
        });

        var viewModel = new DataReportsViewModel(harness.Services, actor);
        await viewModel.LoadAsync();

        var row = Assert.Single(viewModel.Records);
        Assert.Equal("M-200", row.ProductModelName);
    }
}
