using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 记录列表中的一行，其中结论可被界面修改。
/// </summary>
public sealed partial class RecordRow : ObservableObject
{
    public required int Id { get; init; }
    public required int TaskId { get; init; }
    public required string TaskNumber { get; init; }
    public required string StateText { get; init; }
    public required string DeviceModeText { get; init; }
    public required string StartedAt { get; init; }

    /// <summary>
    /// 记录结论文字。
    /// </summary>
    [ObservableProperty]
    private string _conclusion = string.Empty;
}

/// <summary>
/// 报表列表中的一行。
/// </summary>
public sealed class ReportRow
{
    public required int Id { get; init; }
    public required string StatusText { get; init; }
    public required string OutputPath { get; init; }
    public required string CreatedAt { get; init; }
}

/// <summary>
/// 数据与报表页面：记录查询、为已完成记录生成报表、展示报表记录列表。
/// </summary>
public sealed partial class DataReportsViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public DataReportsViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "数据与报表";

    /// <summary>
    /// 页面展示的记录列表。
    /// </summary>
    public ObservableCollection<RecordRow> Records { get; } = new();

    /// <summary>
    /// 页面展示的报表列表。
    /// </summary>
    public ObservableCollection<ReportRow> Reports { get; } = new();

    /// <summary>
    /// 当前选中的记录；变化时自动加载该记录对应的报表。
    /// </summary>
    [ObservableProperty]
    private RecordRow? _selectedRecord;

    partial void OnSelectedRecordChanged(RecordRow? value) => _ = LoadReportsAsync();

    /// <summary>
    /// 页面加载命令：读取记录与任务并刷新记录列表。
    /// </summary>
    [RelayCommand]
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

    /// <summary>
    /// 生成报表命令：为当前选中的记录生成报表文件。
    /// </summary>
    [RelayCommand]
    public async Task GenerateReportAsync() => await GenerateReportForSelectedRowAsync(SelectedRecord);

    private async Task LoadReportsAsync()
    {
        Reports.Clear();
        if (SelectedRecord is null) return;
        var list = await _services.ReportRepository.ListByRecordAsync(SelectedRecord.Id);
        foreach (var r in list)
            Reports.Add(new ReportRow { Id = r.Id, StatusText = r.Status.ToString(), OutputPath = r.OutputPath ?? string.Empty, CreatedAt = r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm") });
    }

    private async Task GenerateReportForSelectedRowAsync(RecordRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null) return;
            var record = await _services.TaskRepository.GetRecordAsync(row.Id) ?? throw new Core.Common.DomainException("记录不存在");
            var task = await _services.TaskRepository.GetAsync(record.TaskId) ?? throw new Core.Common.DomainException("任务不存在");


            var outputDir = Path.Combine(_services.DatabasePath is { Length: > 0 } db ? Path.GetDirectoryName(db) ?? "." : ".", "reports");
            var report = await _services.Reports.GenerateAsync(_actor, row.Id, "assets/report-templates/标准报表.xlsx", outputDir);
            StatusMessage = $"报表已生成：{report.OutputPath}";
            await LoadReportsAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}