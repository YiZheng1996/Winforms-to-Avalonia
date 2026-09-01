using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core;
using XXX.TestBench.Core.Domain;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.App.ViewModels;

public sealed class TaskRow : ObservableObject
{
    public required int Id { get; init; }
    public required string TaskNumber { get; init; }
    public required string ModelCode { get; init; }
    public required int RecipeVersion { get; init; }
    private string _stateText = string.Empty;
    public string StateText { get => _stateText; set => SetField(ref _stateText, value); }
    public required string ProductNumber { get; init; }
    public required string CreatedAt { get; init; }
}

/// <summary>任务管理：列表、新建（类型→型号→已发布配方+产品标识至少一项）、取消 Draft/Ready。</summary>
public sealed class TaskManagementViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public TaskManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
        NewTaskCommand = new RelayCommand(ToggleNewTask);
        CreateTaskCommand = new RelayCommand(CreateTaskAsync);
        CancelSelectedCommand = new RelayCommand(CancelSelectedAsync);
    }

    public override string Title => "任务管理";

    public ObservableCollection<TaskRow> Tasks { get; } = new();
    public ObservableCollection<XXX.TestBench.Core.Domain.Products.ProductType> ProductTypes { get; } = new();
    public ObservableCollection<XXX.TestBench.Core.Domain.Products.ProductModel> ProductModels { get; } = new();
    public ObservableCollection<XXX.TestBench.Core.Domain.Recipes.RecipeVersion> Recipes { get; } = new();

    private bool _showNewTask;
    public bool ShowNewTask { get => _showNewTask; private set => SetField(ref _showNewTask, value); }

    private XXX.TestBench.Core.Domain.Products.ProductType? _selectedType;
    public XXX.TestBench.Core.Domain.Products.ProductType? SelectedType
    {
        get => _selectedType;
        set { if (SetField(ref _selectedType, value)) _ = LoadModelsAsync(); }
    }

    private XXX.TestBench.Core.Domain.Products.ProductModel? _selectedModel;
    public XXX.TestBench.Core.Domain.Products.ProductModel? SelectedModel
    {
        get => _selectedModel;
        set { if (SetField(ref _selectedModel, value)) _ = LoadRecipesAsync(); }
    }

    private XXX.TestBench.Core.Domain.Recipes.RecipeVersion? _selectedRecipe;
    public XXX.TestBench.Core.Domain.Recipes.RecipeVersion? SelectedRecipe { get => _selectedRecipe; set => SetField(ref _selectedRecipe, value); }

    private string _productNumber = string.Empty;
    public string ProductNumber { get => _productNumber; set => SetField(ref _productNumber, value); }
    private string _batchNumber = string.Empty;
    public string BatchNumber { get => _batchNumber; set => SetField(ref _batchNumber, value); }
    private string _stationNumber = string.Empty;
    public string StationNumber { get => _stationNumber; set => SetField(ref _stationNumber, value); }
    private string _remark = string.Empty;
    public string Remark { get => _remark; set => SetField(ref _remark, value); }

    public RelayCommand LoadCommand { get; }
    public RelayCommand NewTaskCommand { get; }
    public RelayCommand CreateTaskCommand { get; }
    public RelayCommand CancelSelectedCommand { get; }

    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var list = await _services.TaskRepository.ListTasksAsync(null, ct);
            var models = await _services.ProductRepository.ListModelsAsync(null, includeDisabled: true, ct);
            Tasks.Clear();
            foreach (var task in list)
            {
                var model = models.FirstOrDefault(m => m.Id == task.ProductModelId);
                var recipe = await _services.RecipeRepository.GetAsync(task.RecipeVersionId, ct);
                Tasks.Add(new TaskRow
                {
                    Id = task.Id,
                    TaskNumber = task.TaskNumber,
                    ModelCode = model?.Code ?? task.ProductModelId.ToString(),
                    RecipeVersion = recipe?.Version ?? 0,
                    StateText = task.State.ToString(),
                    ProductNumber = task.ProductIdentity.ProductNumber ?? string.Empty,
                    CreatedAt = task.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm")
                });
            }
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task LoadTypesAsync()
    {
        ProductTypes.Clear();
        foreach (var t in await _services.ProductRepository.ListTypesAsync(includeDisabled: true)) ProductTypes.Add(t);
    }

    private async Task LoadModelsAsync()
    {
        ProductModels.Clear();
        SelectedModel = null;
        Recipes.Clear();
        if (SelectedType is null) return;
        foreach (var m in await _services.ProductRepository.ListModelsAsync(SelectedType.Id, includeDisabled: true)) ProductModels.Add(m);
    }

    private async Task LoadRecipesAsync()
    {
        Recipes.Clear();
        SelectedRecipe = null;
        if (SelectedModel is null) return;
        foreach (var r in await _services.RecipeRepository.ListByModelAsync(SelectedModel.Id, includeRetired: false))
            if (r.Status == XXX.TestBench.Core.Domain.Recipes.RecipeStatus.Published) Recipes.Add(r);
    }

    private Task ToggleNewTask()
    {
        ShowNewTask = !ShowNewTask;
        if (ShowNewTask) _ = LoadTypesAsync();
        return Task.CompletedTask;
    }

    public async Task CreateTaskAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            var identity = new ProductIdentity(string.IsNullOrWhiteSpace(ProductNumber) ? null : ProductNumber.Trim(),
                string.IsNullOrWhiteSpace(BatchNumber) ? null : BatchNumber.Trim(),
                string.IsNullOrWhiteSpace(StationNumber) ? null : StationNumber.Trim(),
                string.IsNullOrWhiteSpace(Remark) ? null : Remark.Trim());
            if (SelectedModel is null || SelectedRecipe is null)
                throw new Core.Common.DomainException("请选择产品型号与已发布配方");
            await _services.Tasks.CreateAsync(_actor, SelectedModel.Id, SelectedRecipe.Id, identity);
            StatusMessage = "任务创建成功";
            ShowNewTask = false;
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private TaskRow? _selectedTaskRow;
    public TaskRow? SelectedTaskRow { get => _selectedTaskRow; set => SetField(ref _selectedTaskRow, value); }

    public async Task CancelSelectedAsync()
    {
        if (SelectedTaskRow is null) { StatusMessage = "请先在列表选择任务"; return; }
        await CancelTaskAsync(SelectedTaskRow.Id);
    }

    private async Task CancelTaskAsync(int? taskId)
    {
        StatusMessage = string.Empty;
        try
        {
            if (taskId is null) return;
            await _services.Tasks.CancelAsync(_actor, taskId.Value);
            StatusMessage = "任务已取消";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}
