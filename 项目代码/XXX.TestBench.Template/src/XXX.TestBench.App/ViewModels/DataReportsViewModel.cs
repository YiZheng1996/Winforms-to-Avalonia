using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 记录列表中的一行，其中结论可被界面修改。
/// </summary>
public sealed partial class RecordRow : ObservableObject
{
    /// <summary>
    /// 记录编号。
    /// </summary>
    public required int Id { get; init; }
    /// <summary>
    /// 记录流水号。
    /// </summary>
    public required string RecordNumber { get; init; }
    /// <summary>
    /// 产品编号。
    /// </summary>
    public required string ProductNumber { get; init; }
    /// <summary>
    /// 产品型号名称。
    /// </summary>
    public required string ProductModelName { get; init; }
    /// <summary>
    /// 记录状态文字。
    /// </summary>
    public required string StateText { get; init; }
    /// <summary>
    /// 设备模式文字。
    /// </summary>
    public required string DeviceModeText { get; init; }
    /// <summary>
    /// 开始时间文字。
    /// </summary>
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
    /// <summary>
    /// 报表编号。
    /// </summary>
    public required int Id { get; init; }
    /// <summary>
    /// 报表状态文字。
    /// </summary>
    public required string StatusText { get; init; }
    /// <summary>
    /// 输出文件路径。
    /// </summary>
    public required string OutputPath { get; init; }
    /// <summary>
    /// 创建时间文字。
    /// </summary>
    public required string CreatedAt { get; init; }
}

/// <summary>
/// 数据与报表页面：试验记录查询、为已完成记录生成报表、展示报表记录列表。
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
    /// 页面加载命令：读取试验记录并刷新记录列表。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Records.Clear();
            var records = await _services.RecordRepository.ListRecordsAsync(null, ct);
            var models = await _services.ProductRepository.ListModelsAsync(null, includeDisabled: true, ct);
            var modelNames = models.ToDictionary(model => model.Id, model => model.Name);
            foreach (var record in records)
            {
                Records.Add(new RecordRow
                {
                    Id = record.Id,
                    RecordNumber = record.RecordNumber,
                    ProductNumber = record.ProductIdentity.ProductNumber ?? string.Empty,
                    ProductModelName = modelNames.TryGetValue(record.ProductModelId, out var modelName)
                        ? modelName
                        : "未知产品型号",
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

    /// <summary>
    /// 加载当前记录对应的报表列表。
    /// </summary>
    private async Task LoadReportsAsync()
    {
        Reports.Clear();
        if (SelectedRecord is null) return;
        var list = await _services.ReportRepository.ListByRecordAsync(SelectedRecord.Id);
        foreach (var r in list)
            Reports.Add(new ReportRow { Id = r.Id, StatusText = r.Status.ToString(), OutputPath = r.OutputPath ?? string.Empty, CreatedAt = r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm") });
    }

    /// <summary>
    /// 为指定记录生成报表并刷新列表。
    /// </summary>
    private async Task GenerateReportForSelectedRowAsync(RecordRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null) return;
            var record = await _services.RecordRepository.GetRecordAsync(row.Id) ?? throw new Core.Common.DomainException("记录不存在");
            var outputDir = Path.Combine(_services.DatabasePath is { Length: > 0 } db ? Path.GetDirectoryName(db) ?? "." : ".", "reports");
            var report = await _services.Reports.GenerateAsync(_actor, row.Id, "assets/report-templates/标准报表.xlsx", outputDir);
            StatusMessage = $"报表已生成：{report.OutputPath}";
            await LoadReportsAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}
