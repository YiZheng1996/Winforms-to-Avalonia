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

    [Fact]
    public async Task TaskManagement_ListAndCancel()
    {
        var (harness, actor) = await AdminAsync();
        var vm = new TaskManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();
        Assert.Empty(vm.Tasks);

        var productType = await harness.Services.Products.CreateTypeAsync(actor, "PT", "压力试验");
        var model = await harness.Services.Products.CreateModelAsync(actor, productType.Id, "M1", "型号1");
        var item = await harness.Services.Definitions.CreateItemAsync(actor, "IT1", "耐压", "PressureExecutor", "PassFail", 1);
        var param = await harness.Services.Definitions.CreateParameterAsync(actor, item.Id, "Voltage", "试验电压", XXX.TestBench.Core.Domain.TestDefinitions.ParameterDataType.Decimal, true, "kV", 0, 50, null, null, 1);
        var draft = await harness.Services.Recipes.CreateDraftAsync(actor, model.Id, "配方1");
        await harness.Services.Recipes.AddItemAsync(actor, draft.Id, item.Id, 1);
        var recipeItems = await harness.Services.RecipeRepository.ListItemsAsync(draft.Id);
        await harness.Services.Recipes.SetParameterValueAsync(actor, draft.Id, recipeItems[0].Id, param.Id, "0");
        await harness.Services.Recipes.PublishAsync(actor, draft.Id);
        var task = await harness.Services.Tasks.CreateAsync(actor, model.Id, draft.Id, new ProductIdentity("SN001", null, null, null));

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
        var productType = await harness.Services.Products.CreateTypeAsync(actor, "PT", "压力试验");
        var model = await harness.Services.Products.CreateModelAsync(actor, productType.Id, "M1", "型号1");
        var item = await harness.Services.Definitions.CreateItemAsync(actor, "IT1", "耐压", "PressureExecutor", "PassFail", 1);
        var param = await harness.Services.Definitions.CreateParameterAsync(actor, item.Id, "Voltage", "试验电压", XXX.TestBench.Core.Domain.TestDefinitions.ParameterDataType.Decimal, true, "kV", 0, 50, null, null, 1);
        var draft = await harness.Services.Recipes.CreateDraftAsync(actor, model.Id, "配方1");
        await harness.Services.Recipes.AddItemAsync(actor, draft.Id, item.Id, 1);
        var recipeItems = await harness.Services.RecipeRepository.ListItemsAsync(draft.Id);
        await harness.Services.Recipes.SetParameterValueAsync(actor, draft.Id, recipeItems[0].Id, param.Id, "0");
        await harness.Services.Recipes.PublishAsync(actor, draft.Id);
        var task = await harness.Services.Tasks.CreateAsync(actor, model.Id, draft.Id, new ProductIdentity("SN001", null, null, null));
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
    public async Task RecipeCenter_CreatesTypeAndDraft()
    {
        var (harness, actor) = await AdminAsync();
        var vm = new RecipeCenterViewModel(harness.Services, actor);
        await vm.LoadAsync();
        Assert.Empty(vm.Types);

        vm.NewCode = "PT";
        vm.NewName = "压力试验";
        await vm.AddTypeCommand.ExecuteAsync();
        await vm.LoadAsync();
        Assert.Single(vm.Types);
    }
}
