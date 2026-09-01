using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.App.ViewModels;

public sealed class ExecutionItemRow : ObservableObject
{
    public required int RecipeItemId { get; init; }
    public required string Name { get; init; }
    private string _stateText = "Pending";
    public string StateText { get => _stateText; set => SetField(ref _stateText, value); }
    private string? _summary;
    public string? Summary { get => _summary; set => SetField(ref _summary, value); }
    private string? _resultText;
    public string? ResultText { get => _resultText; set => SetField(ref _resultText, value); }
}

public sealed class ExecutionTaskRow
{
    public required int Id { get; init; }
    public required string TaskNumber { get; init; }
    public required string StateText { get; init; }
}

/// <summary>试验执行：选择 Ready 任务→预检启动→逐项执行→全部完成后结束。</summary>
public sealed class TestExecutionViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public TestExecutionViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
        StartCommand = new RelayCommand(StartAsync);
        ExecuteItemCommand = new RelayCommand<ExecutionItemRow>(ExecuteItemAsync);
        FinishCommand = new RelayCommand(FinishAsync);
    }

    public override string Title => "试验执行";

    public ObservableCollection<ExecutionTaskRow> Tasks { get; } = new();
    public ObservableCollection<ExecutionItemRow> Items { get; } = new();

    private ExecutionTaskRow? _selectedTask;
    public ExecutionTaskRow? SelectedTask { get => _selectedTask; set => SetField(ref _selectedTask, value); }

    private int _recordId;

    private string _recordStateText = "未启动";
    public string RecordStateText { get => _recordStateText; private set => SetField(ref _recordStateText, value); }

    private string _conclusion = string.Empty;
    public string Conclusion { get => _conclusion; private set => SetField(ref _conclusion, value); }

    private bool _deviceReady;
    public bool DeviceReady { get => _deviceReady; private set => SetField(ref _deviceReady, value); }

    public RelayCommand LoadCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand<ExecutionItemRow> ExecuteItemCommand { get; }
    public RelayCommand FinishCommand { get; }

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

    private async Task LoadItemsAsync()
    {
        Items.Clear();
        var record = await _services.TaskRepository.GetRecordAsync(_recordId);
        if (record is null) return;
        var recipeItems = await _services.RecipeRepository.ListItemsAsync(record.RecipeVersionId);
        var results = await _services.TaskRepository.ListItemResultsAsync(_recordId);
        foreach (var ri in recipeItems)
        {
            var def = await _services.DefinitionRepository.GetItemAsync(ri.TestItemDefinitionId);
            var result = results.FirstOrDefault(r => r.RecipeItemId == ri.Id);
            Items.Add(new ExecutionItemRow
            {
                RecipeItemId = ri.Id,
                Name = def?.Name ?? ri.TestItemDefinitionId.ToString(),
                StateText = result?.State.ToString() ?? "Pending",
                Summary = result?.SummaryValue,
                ResultText = result?.ResultText
            });
        }
    }

    public async Task ExecuteItemAsync(ExecutionItemRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null) return;
            if (_recordId == 0) throw new Core.Common.DomainException("请先启动试验");
            var runtime = _services.DeviceModes.Runtime ?? throw new Core.Common.DomainException("设备运行时未初始化");
            var result = await _services.Execution.ExecuteItemAsync(_actor, _recordId, row.RecipeItemId, runtime);
            row.StateText = result.State.ToString();
            row.Summary = result.SummaryValue;
            row.ResultText = result.ResultText;
            StatusMessage = $"项点 {row.Name}：{result.State}";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

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
}
