using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 新运行流程接入前的只读页面。旧分步服务已删除，不能创建记录、执行项点或结束试验。
/// W06/W08 完成时再接入共享协调器，不能在此恢复第二套生命周期。
/// </summary>
public sealed partial class TestExecutionViewModel : PageViewModel
{
    private readonly ShellServices _services;
    public const string UnavailableMessage = "试验功能暂不可用，请等待新版试验流程完成。";

    public TestExecutionViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        StatusMessage = UnavailableMessage;
    }

    public override string Title => "试验执行";
    public bool CanRun => false;
    public string RecordStateText => "暂不可用";
    public string Conclusion => string.Empty;
    public ObservableCollection<ProductType> ProductTypes { get; } = new();
    public ObservableCollection<ProductModel> ProductModels { get; } = new();
    public ObservableCollection<ExecutionItemRow> Items { get; } = new();
    [ObservableProperty] private ProductType? _selectedType;
    [ObservableProperty] private ProductModel? _selectedModel;
    [ObservableProperty] private string _productNumberInput = string.Empty;
    [ObservableProperty] private string _batchNumberInput = string.Empty;
    [ObservableProperty] private string _stationNumberInput = string.Empty;
    [ObservableProperty] private string _remarkInput = string.Empty;

    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            ProductTypes.Clear();
            foreach (var type in await _services.ProductRepository.ListTypesAsync(false, ct)) ProductTypes.Add(type);
            SelectedType = ProductTypes.FirstOrDefault();
            ProductModels.Clear();
            if (SelectedType is not null)
                foreach (var model in await _services.ProductRepository.ListModelsAsync(SelectedType.Id, false, ct)) ProductModels.Add(model);
            SelectedModel = ProductModels.FirstOrDefault();
            StatusMessage = UnavailableMessage;
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    // 命令和直接方法调用都不能落入旧执行逻辑。
    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task StartAsync() => ShowUnavailableAsync();
    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task ExecuteItemAsync(ExecutionItemRow? row) => ShowUnavailableAsync();
    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task FinishAsync() => ShowUnavailableAsync();

    private Task ShowUnavailableAsync()
    {
        StatusMessage = UnavailableMessage;
        return Task.CompletedTask;
    }
}
