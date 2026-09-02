using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 试验项执行表格中的一行，状态、摘要与结果可被界面刷新。
/// </summary>
public sealed partial class ExecutionItemRow : ObservableObject
{
    public required int ItemDefinitionId { get; init; }
    public required string Name { get; init; }

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
/// 试验执行任务列表中的一行。
/// </summary>
public sealed class ExecutionTaskRow
{
    public required int Id { get; init; }
    public required string TaskNumber { get; init; }
    public required string StateText { get; init; }
}

/// <summary>
/// 试验执行页面：选择就绪任务、预检启动、逐项执行、全部完成后结束。
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
    /// 页面展示的可执行任务列表。
    /// </summary>
    public ObservableCollection<ExecutionTaskRow> Tasks { get; } = new();

    /// <summary>
    /// 当前试验的执行项列表。
    /// </summary>
    public ObservableCollection<ExecutionItemRow> Items { get; } = new();

    /// <summary>
    /// 当前选中的任务。
    /// </summary>
    [ObservableProperty]
    private ExecutionTaskRow? _selectedTask;

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
    /// 页面加载命令：读取就绪或运行中的任务并刷新设备就绪状态。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Tasks.Clear();
            var list = await _services.TaskRepository.ListTasksAsync(null, ct);
            foreach (var t in list.Where(t => t.State is TaskState.Ready or TaskState.Running))
                Tasks.Add(new ExecutionTaskRow { Id = t.Id, TaskNumber = t.TaskNumber, StateText = t.State.ToString() });
            DeviceReady = _services.DeviceModes.Runtime is not null && _services.DeviceModes.Health == DeviceHealth.Healthy;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 启动命令：预检并启动选中的任务，然后加载执行项列表。
    /// </summary>
    [RelayCommand]
    public async Task StartAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            if (SelectedTask is null) throw new Core.Common.DomainException("请选择任务");
            var runtime = _services.DeviceModes.Runtime ?? throw new Core.Common.DomainException("设备运行时未初始化");
            var record = await _services.Execution.StartAsync(_actor, SelectedTask.Id, _services.DeviceConfig.DeviceMode, runtime);
            _recordId = record.Id;
            RecordStateText = record.State.ToString();
            await LoadItemsAsync();
            StatusMessage = "试验已启动（预检通过）";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

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
            var result = await _services.Execution.ExecuteItemAsync(_actor, _recordId, row.ItemDefinitionId, runtime);
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
            var record = await _services.TaskRepository.GetRecordAsync(_recordId);
            RecordStateText = record?.State.ToString() ?? "Completed";
            StatusMessage = $"试验结束：{Conclusion}";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private async Task LoadItemsAsync()
    {
        Items.Clear();
        var record = await _services.TaskRepository.GetRecordAsync(_recordId);
        if (record is null) return;
        var sequence = await _services.Execution.GetSequenceAsync();
        var results = await _services.TaskRepository.ListItemResultsAsync(_recordId);
        foreach (var def in sequence)
        {
            var result = results.FirstOrDefault(r => r.TestItemDefinitionId == def.Id);
            Items.Add(new ExecutionItemRow
            {
                ItemDefinitionId = def.Id,
                Name = def.Name,
                StateText = result?.State.ToString() ?? "Pending",
                Summary = result?.SummaryValue,
                ResultText = result?.ResultText
            });
        }
    }
}