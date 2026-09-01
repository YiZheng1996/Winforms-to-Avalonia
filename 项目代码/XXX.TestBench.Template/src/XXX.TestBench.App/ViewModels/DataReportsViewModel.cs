using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.App.ViewModels;

public sealed class RecordRow : ObservableObject
{
    public required int Id { get; init; }
    public required int TaskId { get; init; }
    public required string TaskNumber { get; init; }
    public required string StateText { get; init; }
    public required string DeviceModeText { get; init; }
    public required string StartedAt { get; init; }
    private string _conclusion = string.Empty;
    public string Conclusion { get => _conclusion; set => SetField(ref _conclusion, value); }
}

public sealed class ReportRow
{
    public required int Id { get; init; }
    public required string StatusText { get; init; }
    public required string OutputPath { get; init; }
    public required string CreatedAt { get; init; }
}

/// <summary>数据与报表：记录查询、已完成记录生成报表、报表记录列表。</summary>
public sealed class DataReportsViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public DataReportsViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
        GenerateReportCommand = new RelayCommand(GenerateSelectedAsync);
    }

    public override string Title => "数据与报表";

    public ObservableCollection<RecordRow> Records { get; } = new();
    public ObservableCollection<ReportRow> Reports { get; } = new();

    private RecordRow? _selectedRecord;
    public RecordRow? SelectedRecord { get => _selectedRecord; set { if (SetField(ref _selectedRecord, value)) _ = LoadReportsAsync(); } }

    public RelayCommand LoadCommand { get; }
    public RelayCommand GenerateReportCommand { get; }

    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Records.Clear();
            var records = await _services.TaskRepository.ListRecordsAsync(null, ct);
            var tasks = await _services.TaskRepository.ListTasksAsync(null, ct);
            foreach (var record in records)
            {
                var task = tasks.FirstOrDefault(t => t.Id == record.TaskId);
                Records.Add(new RecordRow
                {
                    Id = record.Id,
                    TaskId = record.TaskId,
                    TaskNumber = task?.TaskNumber ?? record.TaskId.ToString(),
                    StateText = record.State.ToString(),
                    DeviceModeText = record.DeviceMode.ToString(),
                    StartedAt = record.StartedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                    Conclusion = record.Conclusion ?? string.Empty
                });
            }
        }
        finally { IsBusy = false; }
    }

    private async Task LoadReportsAsync()
    {
        Reports.Clear();
        if (SelectedRecord is null) return;
        var list = await _services.ReportRepository.ListByRecordAsync(SelectedRecord.Id);
        foreach (var r in list)
            Reports.Add(new ReportRow { Id = r.Id, StatusText = r.Status.ToString(), OutputPath = r.OutputPath ?? string.Empty, CreatedAt = r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm") });
    }

    public async Task GenerateSelectedAsync()
    {
        await GenerateReportAsync(SelectedRecord);
    }

    private async Task GenerateReportAsync(RecordRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null) return;
            var record = await _services.TaskRepository.GetRecordAsync(row.Id) ?? throw new Core.Common.DomainException("记录不存在");
            var task = await _services.TaskRepository.GetAsync(record.TaskId) ?? throw new Core.Common.DomainException("任务不存在");
            var recipe = await _services.RecipeRepository.GetAsync(record.RecipeVersionId);
            var template = recipe?.ReportTemplatePath;
            var outputDir = Path.Combine(_services.DatabasePath is { Length: > 0 } db ? Path.GetDirectoryName(db) ?? "." : ".", "reports");
            var report = await _services.Reports.GenerateAsync(_actor, row.Id, template ?? "assets/report-templates/标准报表.xlsx", outputDir);
            StatusMessage = $"报表已生成：{report.OutputPath}";
            await LoadReportsAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}
