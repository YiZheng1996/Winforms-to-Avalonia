using System.Collections.ObjectModel;
using System.Windows.Input;

namespace XXX.TestBench.Avalonia.ViewModels;

/// <summary>
/// 实现查询条件、选择和分页状态；报表渲染、打印、删除和上传属于尚未接入的外部边界。
/// </summary>
public sealed class ReportsViewModel : ObservableObject
{
    private readonly List<ReportRecordViewModel> _sourceRecords;
    private string _vehicleTypeFilter = string.Empty;
    private string _modelFilter = string.Empty;
    private string _productNumberFilter = string.Empty;
    private string _vehicleNumberFilter = string.Empty;
    private DateTimeOffset? _startDate = DateTimeOffset.Now.Date;
    private DateTimeOffset? _endDate = DateTimeOffset.Now.Date;
    private ReportRecordViewModel? _selectedRecord;
    private int _currentPage = 1;
    private string _feedbackText = "尚未连接 Legacy SQLite；默认查询结果为空。";

    public ReportsViewModel(IEnumerable<ReportRecordViewModel>? sourceRecords = null)
    {
        _sourceRecords = sourceRecords?.ToList() ?? [];
        Records = new ObservableCollection<ReportRecordViewModel>();
        QueryCommand = new RelayCommand(Query);
        PreviousPageCommand = new RelayCommand(PreviousPage, () => CanPreviousPage);
        NextPageCommand = new RelayCommand(NextPage, () => CanNextPage);
        ViewReportCommand = new RelayCommand(ViewReport, () => CanViewReport);
        // 重传和删除属于持久化/上传外部效果；在事务、权限和审计边界完成前，
        // 命令自身固定拒绝执行，不能只依赖 XAML 的 IsEnabled 外观。
        RetryUploadCommand = new RelayCommand(
            () => { },
            () => false);
        DeleteRecordCommand = new RelayCommand(
            () => { },
            () => false);
        Query();
    }

    public ObservableCollection<ReportRecordViewModel> Records { get; }
    public ICommand QueryCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand ViewReportCommand { get; }
    public ICommand RetryUploadCommand { get; }
    public ICommand DeleteRecordCommand { get; }

    public string VehicleTypeFilter
    {
        get => _vehicleTypeFilter;
        set => SetProperty(ref _vehicleTypeFilter, value);
    }

    public string ModelFilter
    {
        get => _modelFilter;
        set => SetProperty(ref _modelFilter, value);
    }

    public string ProductNumberFilter
    {
        get => _productNumberFilter;
        set => SetProperty(ref _productNumberFilter, value);
    }

    public string VehicleNumberFilter
    {
        get => _vehicleNumberFilter;
        set => SetProperty(ref _vehicleNumberFilter, value);
    }

    public DateTimeOffset? StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    public DateTimeOffset? EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    public ReportRecordViewModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (!SetProperty(ref _selectedRecord, value))
                return;
            _currentPage = 1;
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(PreviewText));
            RaisePageState();
        }
    }

    public int CurrentPage => _currentPage;
    public string PreviewText => SelectedRecord is null
        ? "选择一条记录后显示报表页码和文件状态。"
        : $"{SelectedRecord.ProductNumber} · 第 {CurrentPage}/{SelectedRecord.PageCount} 页 · {SelectedRecord.ReportPath}";
    public string QueryResultText => $"共 {Records.Count} 条记录";
    public string FeedbackText => _feedbackText;
    public string PersistenceBoundaryText =>
        "查询条件、选择和翻页行为已迁移；真实 SQLite 查询、报表渲染/打印、删除及人工重传等待平台与持久化适配器。";
    public bool CanPreviousPage => SelectedRecord is not null && _currentPage > 1;
    public bool CanNextPage => SelectedRecord is not null && _currentPage < SelectedRecord.PageCount;
    public bool CanViewReport => SelectedRecord is not null && !string.IsNullOrWhiteSpace(SelectedRecord.ReportPath);
    public bool CanRetryUpload => false;
    public bool CanDeleteRecord => false;

    private void Query()
    {
        if (StartDate.HasValue && EndDate.HasValue && StartDate.Value.Date > EndDate.Value.Date)
        {
            _feedbackText = "开始日期不能晚于结束日期。";
            OnPropertyChanged(nameof(FeedbackText));
            return;
        }

        DateTimeOffset? start = StartDate.HasValue
            ? new DateTimeOffset(StartDate.Value.Date, StartDate.Value.Offset)
            : null;
        DateTimeOffset? exclusiveEnd = EndDate.HasValue
            ? new DateTimeOffset(EndDate.Value.Date.AddDays(1), EndDate.Value.Offset)
            : null;
        var result = _sourceRecords
            .Where(record => Contains(record.VehicleType, VehicleTypeFilter))
            .Where(record => Contains(record.Model, ModelFilter))
            .Where(record => Contains(record.ProductNumber, ProductNumberFilter))
            .Where(record => Contains(record.VehicleNumber, VehicleNumberFilter))
            .Where(record => !start.HasValue || record.TestTime >= start.Value)
            .Where(record => !exclusiveEnd.HasValue || record.TestTime < exclusiveEnd.Value)
            .OrderByDescending(record => record.TestTime)
            .ToList();

        Records.Clear();
        foreach (var record in result)
            Records.Add(record);
        SelectedRecord = null;
        _feedbackText = _sourceRecords.Count == 0
            ? "持久化适配器未接入，未加载任何旧记录。"
            : $"查询完成，共 {Records.Count} 条。";
        OnPropertyChanged(nameof(QueryResultText));
        OnPropertyChanged(nameof(FeedbackText));
    }

    private void PreviousPage()
    {
        if (!CanPreviousPage)
            return;
        _currentPage--;
        NotifyPageChanged();
    }

    private void NextPage()
    {
        if (!CanNextPage)
            return;
        _currentPage++;
        NotifyPageChanged();
    }

    private void ViewReport()
    {
        _feedbackText = SelectedRecord is null
            ? "请先选择记录。"
            : "报表文件已选择；纯托管渲染服务尚未接入，本轮不会调用旧 Office/SumatraPDF。";
        OnPropertyChanged(nameof(FeedbackText));
    }

    private void NotifyPageChanged()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PreviewText));
        RaisePageState();
    }

    private void RaisePageState()
    {
        OnPropertyChanged(nameof(CanPreviousPage));
        OnPropertyChanged(nameof(CanNextPage));
        OnPropertyChanged(nameof(CanViewReport));
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ViewReportCommand).RaiseCanExecuteChanged();
    }

    private static bool Contains(string value, string filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record ReportRecordViewModel(
    int Id,
    string VehicleType,
    string Model,
    string ProductNumber,
    string VehicleNumber,
    string Tester,
    DateTimeOffset TestTime,
    string UploadStatus,
    string ReportPath,
    int PageCount = 1)
{
    public int PageCount { get; init; } = Math.Max(1, PageCount);
    public string TestTimeText => TestTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}
