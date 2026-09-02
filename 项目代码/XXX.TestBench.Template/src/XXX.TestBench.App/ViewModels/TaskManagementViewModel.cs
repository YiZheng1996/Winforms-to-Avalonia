using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core;
using XXX.TestBench.Core.Domain;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 任务列表中的一行，状态文字可被界面刷新。
/// </summary>
public sealed partial class TaskRow : ObservableObject
{
    public required int Id { get; init; }
    public required string TaskNumber { get; init; }
    public required string ModelCode { get; init; }
    public required string ProductNumber { get; init; }
    public required string CreatedAt { get; init; }

    /// <summary>
    /// 任务当前状态的显示文字。
    /// </summary>
    [ObservableProperty]
    private string _stateText = string.Empty;
}

/// <summary>
/// 任务管理页面：任务列表、新建任务（类型到型号，产品标识至少一项）、取消草稿或就绪任务。
/// 试验项顺序由代码固定，参数在启动时固化，因此任务创建不再选择配方。
/// </summary>
public sealed partial class TaskManagementViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public TaskManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "任务管理";

    /// <summary>
    /// 页面展示的任务列表。
    /// </summary>
    public ObservableCollection<TaskRow> Tasks { get; } = new();

    /// <summary>
    /// 新建任务时可选择的产品类型列表。
    /// </summary>
    public ObservableCollection<XXX.TestBench.Core.Domain.Products.ProductType> ProductTypes { get; } = new();

    /// <summary>
    /// 新建任务时可选择的产品型号列表。
    /// </summary>
    public ObservableCollection<XXX.TestBench.Core.Domain.Products.ProductModel> ProductModels { get; } = new();

    private bool _showNewTask;

    /// <summary>
    /// 是否展开新建任务面板。
    /// </summary>
    public bool ShowNewTask { get => _showNewTask; private set => SetProperty(ref _showNewTask, value); }

    /// <summary>
    /// 新建任务时选中的产品类型；变化时自动加载该类型的型号。
    /// </summary>
    [ObservableProperty]
    private XXX.TestBench.Core.Domain.Products.ProductType? _selectedType;

    partial void OnSelectedTypeChanged(XXX.TestBench.Core.Domain.Products.ProductType? value) => _ = LoadModelsAsync();

    /// <summary>
    /// 新建任务时选中的产品型号。
    /// </summary>
    [ObservableProperty]
    private XXX.TestBench.Core.Domain.Products.ProductModel? _selectedModel;

    /// <summary>
    /// 新建任务时录入的产品编号。
    /// </summary>
    [ObservableProperty]
    private string _productNumber = string.Empty;

    /// <summary>
    /// 新建任务时录入的批次号。
    /// </summary>
    [ObservableProperty]
    private string _batchNumber = string.Empty;

    /// <summary>
    /// 新建任务时录入的工位号。
    /// </summary>
    [ObservableProperty]
    private string _stationNumber = string.Empty;

    /// <summary>
    /// 新建任务时录入的备注。
    /// </summary>
    [ObservableProperty]
    private string _remark = string.Empty;

    /// <summary>
    /// 当前在列表中选中的任务行。
    /// </summary>
    [ObservableProperty]
    private TaskRow? _selectedTaskRow;

    /// <summary>
    /// 页面加载命令：读取任务与型号并刷新任务列表。
    /// </summary>
    [RelayCommand]
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
                Tasks.Add(new TaskRow
                {
                    Id = task.Id,
                    TaskNumber = task.TaskNumber,
                    ModelCode = model?.Code ?? task.ProductModelId.ToString(),
                    ProductNumber = task.ProductIdentity.ProductNumber ?? string.Empty,
                    CreatedAt = task.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                    StateText = task.State.ToString()
                });
            }
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 新建任务命令：展开或收起新建面板，展开时刷新产品类型。
    /// </summary>
    [RelayCommand]
    public void NewTask()
    {
        ShowNewTask = !ShowNewTask;
        if (ShowNewTask) _ = LoadTypesAsync();
    }

    /// <summary>
    /// 创建任务命令：按所选型号与产品标识新建任务。
    /// </summary>
    [RelayCommand]
    public async Task CreateTaskAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            var identity = new ProductIdentity(string.IsNullOrWhiteSpace(ProductNumber) ? null : ProductNumber.Trim(),
                string.IsNullOrWhiteSpace(BatchNumber) ? null : BatchNumber.Trim(),
                string.IsNullOrWhiteSpace(StationNumber) ? null : StationNumber.Trim(),
                string.IsNullOrWhiteSpace(Remark) ? null : Remark.Trim());
            if (SelectedModel is null)
                throw new Core.Common.DomainException("请选择产品型号");
            await _services.Tasks.CreateAsync(_actor, SelectedModel.Id, identity);
            StatusMessage = "任务创建成功";
            ShowNewTask = false;
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    /// <summary>
    /// 取消命令：取消当前在列表中选中的任务。
    /// </summary>
    [RelayCommand]
    public async Task CancelSelectedAsync()
    {
        if (SelectedTaskRow is null) { StatusMessage = "请先在列表选择任务"; return; }
        await CancelTaskAsync(SelectedTaskRow.Id);
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
        if (SelectedType is null) return;
        foreach (var m in await _services.ProductRepository.ListModelsAsync(SelectedType.Id, includeDisabled: true)) ProductModels.Add(m);
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
