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
/// 数据与报表页面：查询试验及报告版本，按原冻结输入重试失败报告。
/// </summary>
public sealed partial class DataReportsViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;
    private readonly Func<string?>? _sessionToken;

    public DataReportsViewModel(ShellServices services, UserContext actor, Func<string?>? sessionToken = null)
    {
        _services = services;
        _actor = actor;
        _sessionToken = sessionToken;
        if (services.ReportRetries is null)
            StatusMessage = "报表计算工具或工作目录尚未配置，当前仅支持查询。";
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

    [ObservableProperty]
    private ReportRow? _selectedReport;

    partial void OnSelectedReportChanged(ReportRow? value) => RetryReportCommand.NotifyCanExecuteChanged();

    private bool CanRetryReport() => !IsBusy && SelectedReport?.Status == ReportStatus.Failed
        && CanGenerateReports && _services.ReportRetries is not null && _sessionToken?.Invoke() is not null;

    /// <summary>按原冻结输入新建失败重试版本，由后台队列计算；不重新评价成功记录。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryReport))]
    public async Task RetryReportAsync()
    {
        if (!CanRetryReport()) return;
        var reportId = SelectedReport!.Id;
        IsBusy = true;
        RetryReportCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _services.ReportRetries!.RetryAsync(reportId, _sessionToken!()!);
            StatusMessage = $"报表版本 {result.RevisionNo} 已登记，刷新可查看处理状态。";
            await LoadReportsAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; RetryReportCommand.NotifyCanExecuteChanged(); }
    }

    partial void OnSelectedRecordChanged(RecordRow? value)
    {
        GenerateReportCommand.NotifyCanExecuteChanged();
        _ = LoadReportsAsync();
    }

    /// <summary>
    /// 当前用户是否拥有生成报表权限。
    /// </summary>
    public bool CanGenerateReports => _actor.HasPermission(PermissionCode.GenerateReports);

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
    [RelayCommand(CanExecute = nameof(CanGenerateReport))]
    public Task GenerateReportAsync()
    {
        StatusMessage = "报表生成功能暂不可用，请等待新版报表计算流程完成。";
        return Task.CompletedTask;
    }

    private bool CanGenerateReport() => false;

    /// <summary>
    /// 加载当前记录对应的报表列表。
    /// </summary>
    private async Task LoadReportsAsync()
    {
        SelectedReport = null;
        Reports.Clear();
        if (SelectedRecord is null) return;
        var recordId = SelectedRecord.Id;
        var list = await _services.ReportRepository.ListByRecordAsync(recordId);
        if (SelectedRecord?.Id != recordId) return;
        Reports.Clear();
        foreach (var r in list)
            Reports.Add(new ReportRow { Id = r.Id, Status = r.Status, RevisionNo = r.RevisionNo, Error = r.Error,
                StatusText = r.Status switch { ReportStatus.Pending => "等待生成", ReportStatus.Generating => "生成中/待核查",
                    ReportStatus.Completed => "已完成", ReportStatus.Failed => "生成失败", _ => "未知" },
                OutputPath = r.StorageKey ?? r.OutputPath ?? string.Empty, CreatedAt = r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm") });
    }

}
