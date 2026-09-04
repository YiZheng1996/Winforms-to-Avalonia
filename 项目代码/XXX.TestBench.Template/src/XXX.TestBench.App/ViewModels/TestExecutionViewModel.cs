using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 试验项执行表格中的一行，状态、摘要与结果可被界面刷新。
/// </summary>
public sealed partial class ExecutionItemRow : ObservableObject
{
    public required int PointId { get; init; }
    public required string Name { get; init; }
    public required string ExecutorCode { get; init; }
    public required int SortOrder { get; init; }

    /// <summary>
    /// 试验项当前状态的显示文字。
    /// </summary>
    [ObservableProperty]
    private string _stateText = "Pending";

    /// <summary>
    /// 试验项的汇总值文字。
    /// </summary>
    [ObservableProperty]
    private string? _summary;

    /// <summary>
    /// 试验项的结果文字。
    /// </summary>
    [ObservableProperty]
    private string? _resultText;
}

/// <summary>
/// 试验执行页面：选择产品类型与产品型号、填写产品标识，预检后直接启动试验记录，
/// 按型号的项点配置逐项执行，全部完成后结束。不存在任务概念。
/// </summary>
public sealed partial class TestExecutionViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public TestExecutionViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "试验执行";

    /// <summary>
    /// 可执行的产品类型列表。
    /// </summary>
    public ObservableCollection<ProductType> ProductTypes { get; } = new();

    /// <summary>
    /// 当前产品类型下的产品型号列表。
    /// </summary>
    public ObservableCollection<ProductModel> ProductModels { get; } = new();

    /// <summary>
    /// 当前试验的项点序列。
    /// </summary>
    public ObservableCollection<ExecutionItemRow> Items { get; } = new();

    /// <summary>
    /// 当前选中的产品类型。
    /// </summary>
    [ObservableProperty]
    private ProductType? _selectedType;

    partial void OnSelectedTypeChanged(ProductType? value) => _ = LoadModelsAsync();

    /// <summary>
    /// 当前选中的产品型号。
    /// </summary>
    [ObservableProperty]
    private ProductModel? _selectedModel;

    /// <summary>
    /// 产品编号输入。
    /// </summary>
    [ObservableProperty]
    private string _productNumberInput = string.Empty;

    /// <summary>
    /// 批次号输入。
    /// </summary>
    [ObservableProperty]
    private string _batchNumberInput = string.Empty;

    /// <summary>
    /// 工位号输入。
    /// </summary>
    [ObservableProperty]
    private string _stationNumberInput = string.Empty;

    /// <summary>
    /// 备注输入。
    /// </summary>
    [ObservableProperty]
    private string _remarkInput = string.Empty;

    private int _recordId;

    private string _recordStateText = "未启动";

    /// <summary>
    /// 当前试验记录的状态文字。
    /// </summary>
    public string RecordStateText { get => _recordStateText; private set => SetProperty(ref _recordStateText, value); }

    private string _conclusion = string.Empty;

    /// <summary>
    /// 当前试验结束后的结论文字。
    /// </summary>
    public string Conclusion { get => _conclusion; private set => SetProperty(ref _conclusion, value); }

    private bool _deviceReady;

    /// <summary>
    /// 设备是否就绪，决定能否启动试验。
    /// </summary>
    public bool DeviceReady { get => _deviceReady; private set => SetProperty(ref _deviceReady, value); }

    /// <summary>
    /// 页面加载命令：加载产品类型与型号并刷新设备就绪状态。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await LoadTypesAsync(ct);
            await LoadModelsAsync(ct);
            DeviceReady = _services.DeviceModes.Runtime is not null && _services.DeviceModes.Health == DeviceHealth.Healthy;
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task LoadTypesAsync(CancellationToken ct = default)
    {
        var selectedId = SelectedType?.Id;
        ProductTypes.Clear();
        var types = await _services.ProductRepository.ListTypesAsync(includeDisabled: false, ct);
        foreach (var type in types) ProductTypes.Add(type);
        if (selectedId is not null)
            SelectedType = ProductTypes.FirstOrDefault(t => t.Id == selectedId);
        else if (SelectedType is null && ProductTypes.Count > 0)
            SelectedType = ProductTypes[0];
    }

    private async Task LoadModelsAsync(CancellationToken ct = default)
    {
        var selectedId = SelectedModel?.Id;
        ProductModels.Clear();
        if (SelectedType is not null)
        {
            var models = await _services.ProductRepository.ListModelsAsync(SelectedType.Id, includeDisabled: false, ct);
            foreach (var model in models) ProductModels.Add(model);
        }
        if (selectedId is not null)
            SelectedModel = ProductModels.FirstOrDefault(m => m.Id == selectedId);
        else if (SelectedModel is null && ProductModels.Count > 0)
            SelectedModel = ProductModels[0];
    }

    /// <summary>
    /// 启动命令：预检并直接创建试验记录，然后加载型号的项点序列。
    /// </summary>
    [RelayCommand]
    public async Task StartAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            if (SelectedModel is null) throw new Core.Common.DomainException("请选择产品型号");
            var identity = BuildIdentity();
            if (!identity.HasAnyValue) throw new Core.Common.DomainException("至少填写一项产品标识（产品编号/批次号/工位号/备注）");
            var runtime = _services.DeviceModes.Runtime ?? throw new Core.Common.DomainException("设备运行时未初始化");
            var record = await _services.Execution.StartAsync(_actor, SelectedModel.Id, identity, _services.DeviceConfig.DeviceMode, runtime);
            _recordId = record.Id;
            RecordStateText = record.State.ToString();
            Conclusion = string.Empty;
            await LoadItemsAsync();
            StatusMessage = "试验已启动（预检通过）";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private ProductIdentity BuildIdentity() => new(
        string.IsNullOrWhiteSpace(ProductNumberInput) ? null : ProductNumberInput.Trim(),
        string.IsNullOrWhiteSpace(BatchNumberInput) ? null : BatchNumberInput.Trim(),
        string.IsNullOrWhiteSpace(StationNumberInput) ? null : StationNumberInput.Trim(),
        string.IsNullOrWhiteSpace(RemarkInput) ? null : RemarkInput.Trim());

    /// <summary>
    /// 执行命令：执行表格中指定的一项试验。
    /// </summary>
    [RelayCommand]
    public async Task ExecuteItemAsync(ExecutionItemRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null) return;
            if (_recordId == 0) throw new Core.Common.DomainException("请先启动试验");
            var runtime = _services.DeviceModes.Runtime ?? throw new Core.Common.DomainException("设备运行时未初始化");
            var result = await _services.Execution.ExecuteItemAsync(_actor, _recordId, row.PointId, runtime);
            row.StateText = result.State.ToString();
            row.Summary = result.SummaryValue;
            row.ResultText = result.ResultText;
            StatusMessage = $"项点 {row.Name}：{result.State}";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    /// <summary>
    /// 结束命令：完成当前试验并展示结论。
    /// </summary>
    [RelayCommand]
    public async Task FinishAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            if (_recordId == 0) throw new Core.Common.DomainException("请先启动试验");
            Conclusion = await _services.Execution.CompleteAsync(_actor, _recordId);
            var record = await _services.RecordRepository.GetRecordAsync(_recordId);
            RecordStateText = record?.State.ToString() ?? "Completed";
            StatusMessage = $"试验结束：{Conclusion}";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private async Task LoadItemsAsync()
    {
        Items.Clear();
        var sequence = await _services.Execution.GetSequenceAsync(_recordId);
        var results = await _services.RecordRepository.ListItemResultsAsync(_recordId);
        foreach (var item in sequence)
        {
            var result = results.FirstOrDefault(r => r.TestItemPointId == item.PointId);
            Items.Add(new ExecutionItemRow
            {
                PointId = item.PointId,
                Name = item.Name,
                ExecutorCode = item.ExecutorCode,
                SortOrder = item.SortOrder,
                StateText = result?.State.ToString() ?? "Pending",
                Summary = result?.SummaryValue,
                ResultText = result?.ResultText
            });
        }
    }
}
