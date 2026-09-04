using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using Xunit;

namespace XXX.TestBench.App.Tests;

public class PageViewModelTests
{
    private static async Task<(AppTestHarness Harness, UserContext Actor)> AdminAsync()
    {
        var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        return (harness, actor);
    }

    private static async Task<(int TypeId, int ModelId)> SeedProductAsync(AppTestHarness harness, UserContext actor)
    {
        var type = await harness.Services.Products.CreateTypeAsync(actor, "压力试验");
        var model = await harness.Services.Products.CreateModelAsync(actor, type.Id, "型号1");
        return (type.Id, model.Id);
    }

    private static async Task<int> SeedPointAndConfigAsync(AppTestHarness harness, UserContext actor, int typeId, int modelId)
    {
        var point = await harness.Services.TestPoints.CreatePointAsync(actor, typeId, "耐压试验", "PressureExecutor", "PassFail", 1);
        await harness.Services.TestPoints.SaveConfigurationAsync(actor, modelId, new[] { point.Id });
        return point.Id;
    }

    private static async Task SeedParametersAsync(AppTestHarness harness, UserContext actor, int typeId, int modelId)
    {
        await harness.Services.Parameters.SaveProjectAsync(actor, 60);
        await harness.Services.Parameters.SaveTypeAsync(actor, typeId, 5000);
        await harness.Services.Parameters.SaveModelAsync(actor, modelId, 100);
    }

    [Fact]
    public async Task TestPointManagement_CreatesPoint()
    {
        var (harness, actor) = await AdminAsync();
        var (typeId, _) = await SeedProductAsync(harness, actor);

        var vm = new TestPointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        Assert.Single(vm.TypeOptions);
        Assert.Empty(vm.Points);

        await vm.CreateFromDialogAsync(new TestPointDialogResult("耐压试验", "PressureExecutor", "PassFail", 1, true));

        Assert.Single(vm.Points);
        Assert.Equal("耐压试验", vm.Points[0].Name);
        Assert.Equal("PressureExecutor", vm.Points[0].ExecutorCode);
    }

    [Fact]
    public async Task PointConfiguration_SaveSequence()
    {
        var (harness, actor) = await AdminAsync();
        var (typeId, modelId) = await SeedProductAsync(harness, actor);
        var point = await harness.Services.TestPoints.CreatePointAsync(actor, typeId, "耐压试验", "PressureExecutor", "PassFail", 1);

        var vm = new PointConfigurationViewModel(harness.Services, actor);
        await vm.LoadAsync();
        Assert.Single(vm.AvailablePoints);
        Assert.Empty(vm.ConfiguredPoints);

        vm.SelectedAvailable = vm.AvailablePoints[0];
        await vm.AddSelectedCommand.ExecuteAsync(null);
        Assert.Single(vm.ConfiguredPoints);

        await vm.SaveCommand.ExecuteAsync(null);
        var sequence = await harness.Services.TestPoints.GetSequenceAsync(modelId);
        Assert.Single(sequence);
        Assert.Equal(point.Id, sequence[0].Id);
    }

    [Fact]
    public async Task TestExecution_FullRun()
    {
        var (harness, actor) = await AdminAsync();
        var (typeId, modelId) = await SeedProductAsync(harness, actor);
        await SeedParametersAsync(harness, actor, typeId, modelId);
        await SeedPointAndConfigAsync(harness, actor, typeId, modelId);

        var vm = new TestExecutionViewModel(harness.Services, actor);
        await vm.LoadAsync();
        Assert.Single(vm.ProductTypes);
        Assert.Single(vm.ProductModels);
        Assert.True(vm.DeviceReady);

        vm.ProductNumberInput = "SN001";
        await vm.StartAsync();
        Assert.Equal("Running", vm.RecordStateText);
        Assert.Single(vm.Items);

        await vm.ExecuteItemAsync(vm.Items[0]);
        Assert.Equal("Passed", vm.Items[0].StateText);

        await vm.FinishAsync();
        Assert.Equal("Completed", vm.RecordStateText);
        Assert.Contains("全部通过", vm.Conclusion);
    }

    [Fact]
    public async Task ParameterManagement_CreatesTypeAndSavesFixedParameters()
    {
        var (harness, actor) = await AdminAsync();
        var vm = new ParameterManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        Assert.Empty(vm.Types);

        await vm.CreateTypeFromDialogAsync(new ProductMasterDataDialogResult("压力试验", null));
        await vm.LoadAsync();
        Assert.Single(vm.Types);

        var type = vm.Types[0];
        var model = await harness.Services.Products.CreateModelAsync(actor, type.Id, "型号1");

        vm.TestTimeInput = "60";
        await vm.SaveProjectParameterCommand.ExecuteAsync(null);
        Assert.Equal("60", (await harness.Services.TestParameterRepository.GetProjectAsync())!.TestTimeSeconds.ToString());

        vm.SelectedParamType = type;
        vm.TestVoltageInput = "5000";
        await vm.SaveTypeParameterCommand.ExecuteAsync(null);
        Assert.Equal(5000.0, (await harness.Services.TestParameterRepository.GetTypeAsync(type.Id))!.TestVoltageV);

        await vm.LoadParamModelOptionsAsync();
        vm.SelectedParamModel = model;
        vm.ProtectCurrentInput = "100";
        await vm.SaveModelParameterCommand.ExecuteAsync(null);
        Assert.Equal(100.0, (await harness.Services.TestParameterRepository.GetModelAsync(model.Id))!.ProtectCurrentMa);
    }

    [Fact]
    public async Task ParameterManagement_DeletesUnusedModelAndType()
    {
        var (harness, actor) = await AdminAsync();
        var (typeId, modelId) = await SeedProductAsync(harness, actor);
        await harness.Services.Parameters.SaveTypeAsync(actor, typeId, 5000);
        await harness.Services.Parameters.SaveModelAsync(actor, modelId, 100);
        var pointId = await SeedPointAndConfigAsync(harness, actor, typeId, modelId);

        var vm = new ParameterManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        vm.SelectedModel = await harness.Services.ProductRepository.GetModelAsync(modelId);
        await vm.DeleteSelectedModelCommand.ExecuteAsync(null);

        Assert.Null(await harness.Services.ProductRepository.GetModelAsync(modelId));
        Assert.Null(await harness.Services.TestParameterRepository.GetModelAsync(modelId));
        Assert.Empty(await harness.Services.ModelPointConfigRepository.ListByModelAsync(modelId));

        await harness.Services.TestPoints.DeletePointAsync(actor, pointId);
        vm.SelectedType = vm.Types.Single(type => type.Id == typeId);
        await vm.DeleteSelectedTypeCommand.ExecuteAsync(null);

        Assert.Null(await harness.Services.ProductRepository.GetTypeAsync(typeId));
        Assert.Null(await harness.Services.TestParameterRepository.GetTypeAsync(typeId));
    }

    [Fact]
    public async Task ParameterManagement_EditsTypeAndModel()
    {
        var (harness, actor) = await AdminAsync();
        var (typeId, modelId) = await SeedProductAsync(harness, actor);
        var vm = new ParameterManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        vm.SelectedType = vm.Types.Single(type => type.Id == typeId);
        await vm.UpdateTypeFromDialogAsync(typeId, new ProductMasterDataDialogResult("绝缘试验", null));
        Assert.Equal("绝缘试验", (await harness.Services.ProductRepository.GetTypeAsync(typeId))!.Name);

        vm.SelectedModel = await harness.Services.ProductRepository.GetModelAsync(modelId);
        await vm.UpdateModelFromDialogAsync(modelId, new ProductMasterDataDialogResult("型号2", typeId));

        var updatedModel = await harness.Services.ProductRepository.GetModelAsync(modelId);
        Assert.NotNull(updatedModel);
        Assert.Equal("型号2", updatedModel!.Name);
        Assert.Equal(typeId, updatedModel.ProductTypeId);
    }
}
