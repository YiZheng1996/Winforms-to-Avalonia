using System.Collections.ObjectModel;
using System.Windows.Input;
using XXX.TestBench.Avalonia.Composition;
using XXX.TestBench.Gateway.Domain;

namespace XXX.TestBench.Avalonia.ViewModels;

/// <summary>
/// 保存当前进程的有界日志视图，并保留 Legacy 停用维护入口的可追溯状态。
/// </summary>
public sealed class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private const int MaximumLogEntries = 500;
    private const string MissingLegacyFieldText = "—";

    private readonly UiGatewayLogSink? _log;
    private readonly List<DiagnosticLogEntryViewModel> _allEntries = [];
    private LogLevelOptionViewModel _selectedLevel = null!;
    private DateTimeOffset? _selectedDate = DateTimeOffset.Now.Date;
    private DateTime? _appliedLocalDate = DateTime.Today;
    private string _searchText = string.Empty;
    private string _appliedSearchText = string.Empty;
    private string _feedbackText = "运行日志来自当前 Avalonia/Gateway 会话。";

    public DiagnosticsViewModel(UiGatewayLogSink? log)
    {
        _log = log;
        if (_log is not null)
            _log.EntryWritten += OnLogEntry;

        // 下拉框显示 Legacy 中文名称，Value 保留原 LogType 英文值，
        // 避免把本地化文本反向传入筛选规则。
        Levels = new ReadOnlyCollection<LogLevelOptionViewModel>(
        [
            new("全部等级", "All"),
            new("跟踪", "Trace"),
            new("调试", "Debug"),
            new("信息", "Info"),
            new("警告", "Warn"),
            new("错误", "Error"),
            new("致命", "Fatal")
        ]);
        _selectedLevel = Levels[0];

        Entries = new ObservableCollection<DiagnosticLogEntryViewModel>();
        MaintenanceFeatures = new ReadOnlyCollection<MaintenanceFeatureViewModel>(
        [
            new("修改密码", "frmChangePwd / frmRemindEdit", "认证服务未接入", false),
            new("设备检查", "frmDeviceInspect + 编辑窗体", "Legacy 主菜单项目级隐藏", false),
            new("维保计量", "frmMeteringRemind + 编辑窗体", "Legacy 主菜单项目级隐藏", false),
            new("问题统计", "frmErrStatistics + 编辑窗体", "Legacy 主菜单项目级隐藏", false)
        ]);
        FilterCommand = new RelayCommand(ApplySearch);
    }

    public IReadOnlyList<LogLevelOptionViewModel> Levels { get; }
    public ObservableCollection<DiagnosticLogEntryViewModel> Entries { get; }
    public IReadOnlyList<MaintenanceFeatureViewModel> MaintenanceFeatures { get; }
    public ICommand FilterCommand { get; }

    public LogLevelOptionViewModel SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (value is not null && SetProperty(ref _selectedLevel, value))
                RefreshEntries();
        }
    }

    /// <summary>
    /// 日期是待查询条件；仅修改日期不刷新，保持 frmNLogs 的“搜索后应用”语义。
    /// </summary>
    public DateTimeOffset? SelectedDate
    {
        get => _selectedDate;
        set => SetProperty(ref _selectedDate, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string FeedbackText => _feedbackText;
    public string ResultText => $"显示 {Entries.Count} / {_allEntries.Count} 条";
    public string KeywordEnhancementText =>
        "关键字是 Avalonia 增强功能；Legacy 搜索按钮原本只应用日期条件。";
    public string MaintenanceBoundaryText =>
        "设备检查、维保计量和问题统计在 Legacy 主菜单中被项目级开关隐藏；本页只保留可追溯入口和字段合同，不擅自启用已停用能力。";

    public void AddEntry(GatewayLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var normalizedLevel = NormalizeLevel(entry.Level.ToString());
        _allEntries.Add(new DiagnosticLogEntryViewModel(
            entry.Timestamp,
            normalizedLevel,
            FindLevelDisplayName(normalizedLevel),
            DisplayOrDash(ReadProperty(entry.Properties, "UserName")),
            DisplayOrDash(ReadProperty(entry.Properties, "MessageName")),
            DisplayOrDash(ReadProperty(entry.Properties, "Source")),
            DisplayOrDash(entry.Component),
            DisplayOrDash(entry.Message),
            entry.Exception));

        // 日志页只保留最近到达的 500 条，防止长时间运行时 UI 内存无界增长。
        if (_allEntries.Count > MaximumLogEntries)
            _allEntries.RemoveAt(0);

        RefreshEntries();
    }

    public void Dispose()
    {
        if (_log is not null)
            _log.EntryWritten -= OnLogEntry;
    }

    private void OnLogEntry(GatewayLogEntry entry) => AddEntry(entry);

    private void ApplySearch()
    {
        // 分离“正在编辑”和“已应用”的查询条件，否则仅改日期或关键字
        // 就会偷偷改变结果，与 Legacy 按钮行为不一致。
        _appliedLocalDate = SelectedDate?.LocalDateTime.Date;
        _appliedSearchText = SearchText.Trim();
        RefreshEntries();
    }

    private void RefreshEntries()
    {
        var selected = _allEntries
            .Where(IsInAppliedLocalDay)
            .Where(entry => LevelMatches(entry.Level, SelectedLevel.Value))
            .Where(ContainsAppliedKeyword)
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();

        Entries.Clear();
        foreach (var entry in selected)
            Entries.Add(entry);

        _feedbackText = $"日志筛选完成：{selected.Count} 条。";
        OnPropertyChanged(nameof(FeedbackText));
        OnPropertyChanged(nameof(ResultText));
    }

    private bool IsInAppliedLocalDay(DiagnosticLogEntryViewModel entry)
    {
        if (_appliedLocalDate is null)
            return true;

        // Legacy 使用 00:00:00～23:59:59；迁移后改为 [day, next day)，
        // 这样不会漏掉 23:59:59 之后的毫秒/时钟精度数据。
        var start = _appliedLocalDate.Value;
        var end = start.AddDays(1);
        var localTimestamp = entry.Timestamp.LocalDateTime;
        return localTimestamp >= start && localTimestamp < end;
    }

    private bool ContainsAppliedKeyword(DiagnosticLogEntryViewModel entry)
    {
        if (string.IsNullOrWhiteSpace(_appliedSearchText))
            return true;

        return entry.Component.Contains(_appliedSearchText, StringComparison.OrdinalIgnoreCase)
               || entry.Message.Contains(_appliedSearchText, StringComparison.OrdinalIgnoreCase)
               || entry.UserName.Contains(_appliedSearchText, StringComparison.OrdinalIgnoreCase)
               || entry.OperationInformation.Contains(_appliedSearchText, StringComparison.OrdinalIgnoreCase)
               || entry.Source.Contains(_appliedSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private string FindLevelDisplayName(string level) =>
        Levels.FirstOrDefault(option => string.Equals(option.Value, level, StringComparison.Ordinal))?.DisplayName
        ?? level;

    private static string NormalizeLevel(string level) => level switch
    {
        // Gateway 使用 Microsoft 风格的 Information/Warning；Legacy 筛选值是 Info/Warn。
        "Information" => "Info",
        "Warning" => "Warn",
        _ => level
    };

    private static bool LevelMatches(string actualLevel, string selectedLevel) =>
        selectedLevel == "All"
        || string.Equals(actualLevel, selectedLevel, StringComparison.Ordinal);

    private static string? ReadProperty(IReadOnlyDictionary<string, string?> properties, string name)
    {
        if (properties.TryGetValue(name, out var exactValue))
            return exactValue;

        // 兼容不同日志适配器的键大小写，但不从其他字段猜测用户或来源。
        foreach (var pair in properties)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    private static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? MissingLegacyFieldText : value.Trim();
}

public sealed record LogLevelOptionViewModel(string DisplayName, string Value);

public sealed record DiagnosticLogEntryViewModel(
    DateTimeOffset Timestamp,
    string Level,
    string LevelDisplayName,
    string UserName,
    string OperationInformation,
    string Source,
    string Component,
    string Message,
    string? Exception)
{
    public string TimestampText => Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string GatewayDetailText => $"{Component} · {Message}";
}

public sealed record MaintenanceFeatureViewModel(
    string Name,
    string LegacyEvidence,
    string StatusText,
    bool IsEnabled);
