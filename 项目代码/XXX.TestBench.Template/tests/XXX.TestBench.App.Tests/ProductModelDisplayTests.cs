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
    public async Task ProcessProductContext_RequiresCompleteInput_AndInvalidatesAfterEditing()
    {
        using var harness = AppTestHarness.Create();
        var shell = new ShellViewModel(harness.Services);
        shell.SelectProductModel(new ProductModelSelectionOption(
            ProductTypeId: 11,
            ProductTypeName: "气密试验",
            ProductModelId: 27,
            ProductModelName: "TB-2026A",
            IsEnabled: true,
            CreatedAtUtc: DateTime.UtcNow));

        var incomplete = await shell.TryConfirmProductContextAsync();

        Assert.False(incomplete.Succeeded);
        Assert.False(shell.IsProductContextConfirmed);

        shell.ProductNumberInput = " CP20260916001 ";
        shell.VehicleNumberInput = " CR400AF-0217 ";
        var confirmed = await shell.TryConfirmProductContextAsync();

        Assert.True(confirmed.Succeeded);
        Assert.True(shell.IsProductContextConfirmed);
        Assert.Equal("CP20260916001", shell.CurrentProductNumberText);
        Assert.Equal("CR400AF-0217", shell.VehicleNumberInput);
        Assert.Equal("产品信息已确认", shell.ProductContextStatusText);

        shell.VehicleNumberInput = "CR400AF-0218";

        Assert.False(shell.IsProductContextConfirmed);
        Assert.Equal("产品信息待确认", shell.ProductContextStatusText);
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
            RunId = Guid.NewGuid().ToString("D"),
            OperationId = Guid.NewGuid().ToString("D"),
            StartRequestId = Guid.NewGuid().ToString("D"),
            BenchId = "bench-test", NodeId = "node-test", Scope = RunScope.Auto,
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
        viewModel.SelectedRecord = row;
        Assert.False(viewModel.GenerateReportCommand.CanExecute(null));
        await viewModel.GenerateReportAsync();
        Assert.Contains("暂不可用", viewModel.StatusMessage);
        Assert.Empty(await harness.Services.ReportRepository.ListByRecordAsync(row.Id));
    }
}
