using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;
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
        var type = await harness.Services.Products.CreateTypeAsync(actor, "PT", "压力试验");
        var model = await harness.Services.Products.CreateModelAsync(actor, type.Id, "M1", "型号1");
        return (type.Id, model.Id);
    }

    private static async Task SeedParametersAsync(AppTestHarness harness, UserContext actor, int typeId, int modelId)
    {
        await harness.Services.Parameters.SaveProjectAsync(actor, 60);
        await harness.Services.Parameters.SaveTypeAsync(actor, typeId, 5000);
        await harness.Services.Parameters.SaveModelAsync(actor, modelId, 100);
    }

    [Fact]
    public async Task TaskManagement_ListAndCancel()
    {
        var (harness, actor) = await AdminAsync();
        var vm = new TaskManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();
        Assert.Empty(vm.Tasks);

        var (_, modelId) = await SeedProductAsync(harness, actor);
        var task = await harness.Services.Tasks.CreateAsync(actor, modelId, new ProductIdentity("SN001", null, null, null));

        await vm.LoadAsync();
        Assert.Single(vm.Tasks);
        Assert.Equal(task.TaskNumber, vm.Tasks[0].TaskNumber);

        vm.SelectedTaskRow = vm.Tasks[0];
        await vm.CancelSelectedAsync();
        await vm.LoadAsync();
        Assert.Equal("Cancelled", vm.Tasks[0].StateText);
    }

    [Fact]
    public async Task TestExecution_FullRun()
    {
        var (harness, actor) = await AdminAsync();
        var (typeId, modelId) = await SeedProductAsync(harness, actor);
        await SeedParametersAsync(harness, actor, typeId, modelId);
        var task = await harness.Services.Tasks.CreateAsync(actor, modelId, new ProductIdentity("SN001", null, null, null));
        await harness.Services.Tasks.ToReadyAsync(actor, task.Id);

        var vm = new TestExecutionViewModel(harness.Services, actor);
        await vm.LoadAsync();
        Assert.Single(vm.Tasks);
        Assert.True(vm.DeviceReady);

        vm.SelectedTask = vm.Tasks[0];
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

        vm.NewCode = "PT";
        vm.NewName = "压力试验";
        await vm.AddTypeCommand.ExecuteAsync(null);
        await vm.LoadAsync();
        Assert.Single(vm.Types);

        var type = vm.Types[0];
        var model = await harness.Services.Products.CreateModelAsync(actor, type.Id, "M1", "型号1");

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
}