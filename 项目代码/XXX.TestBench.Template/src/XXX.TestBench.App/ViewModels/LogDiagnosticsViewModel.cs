using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.App.Localization;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 日志表格中的一行。
/// </summary>
public sealed class LogRow
{
    /// <summary>
    /// 时间文字。
    /// </summary>
    public required string Time { get; init; }
    /// <summary>
    /// 操作者。
    /// </summary>
    public required string Actor { get; init; }
    /// <summary>
    /// 动作名称。
    /// </summary>
    public required string Action { get; init; }
    /// <summary>
    /// 操作目标。
    /// </summary>
    public required string Target { get; init; }
    /// <summary>
    /// 操作详情。
    /// </summary>
    public required string Detail { get; init; }
}

/// <summary>
/// 日志动作筛选项。
/// </summary>
public sealed record LogActionOption(string? Code, string DisplayName);

/// <summary>
/// 日志诊断页面：只读展示审计日志列表。
/// </summary>
public sealed partial class LogDiagnosticsViewModel : PageViewModel
{
    private const int DefaultDateRangeDays = 7;
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public LogDiagnosticsViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;

        ActionOptions.Add(new LogActionOption(null, "全部操作"));
        foreach (var action in LogTextLocalizer.KnownActions)
            ActionOptions.Add(new LogActionOption(action.Key, action.Value));
        SelectedAction = ActionOptions[0];
        ApplyDefaultDateRange();
    }

    public override string Title => "日志诊断";

    /// <summary>
    /// 页面展示的日志行集合。
    /// </summary>
    public ObservableCollection<LogRow> Logs { get; } = new();

    /// <summary>
    /// 可供选择的操作类型。
    /// </summary>
    public ObservableCollection<LogActionOption> ActionOptions { get; } = new();

    /// <summary>
    /// 操作者筛选，支持按账号或系统保留字模糊查询。
    /// </summary>
    [ObservableProperty]
    private string _actorFilter = string.Empty;

    /// <summary>
    /// 操作类型筛选。
    /// </summary>
    [ObservableProperty]
    private LogActionOption? _selectedAction;

    /// <summary>
    /// 对象或详细信息筛选，支持模糊查询。
    /// </summary>
    [ObservableProperty]
    private string _textFilter = string.Empty;

    /// <summary>
    /// 开始日期，按本机日期的零点换算为 UTC 查询。
    /// </summary>
    [ObservableProperty]
    private DateTime? _startDate;

    /// <summary>
    /// 结束日期，包含整天，按下一天零点作为 UTC 开区间。
    /// </summary>
    [ObservableProperty]
    private DateTime? _endDate;

    /// <summary>
    /// 页面加载命令：按当前筛选条件读取日志并刷新列表。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            StatusMessage = string.Empty;
            var query = BuildQuery();
            if (query is null)
            {
                Logs.Clear();
                return;
            }

            var entries = await _services.AuditLog.SearchAsync(query, ct);
            Logs.Clear();
            foreach (var e in entries
                .OrderByDescending(entry => entry.CreatedAtUtc)
                .ThenByDescending(entry => entry.Id))
            {
                Logs.Add(new LogRow
                {
                    Time = ToLocalDateTime(e.CreatedAtUtc).ToString("yyyy-MM-dd HH:mm:ss"),
                    Actor = LogTextLocalizer.Actor(e.Actor),
                    Action = LogTextLocalizer.Action(e.Action),
                    Target = LogTextLocalizer.Target(e.Action, e.Target),
                    Detail = LogTextLocalizer.Detail(e.Action, e.Detail)
                });
            }

            StatusMessage = $"查询到 {Logs.Count} 条日志";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 使用当前筛选条件重新查询日志。
    /// </summary>
    [RelayCommand]
    public async Task SearchAsync(CancellationToken ct = default) => await LoadAsync(ct);

    /// <summary>
    /// 恢复默认筛选条件并重新加载最近 7 天日志。
    /// </summary>
    [RelayCommand]
    public async Task ResetFiltersAsync(CancellationToken ct = default)
    {
        ActorFilter = string.Empty;
        SelectedAction = ActionOptions[0];
        TextFilter = string.Empty;
        ApplyDefaultDateRange();
        await LoadAsync(ct);
    }

    private void ApplyDefaultDateRange()
    {
        var today = DateTime.Today;
        StartDate = today.AddDays(-(DefaultDateRangeDays - 1));
        EndDate = today;
    }

    private AuditLogQuery? BuildQuery()
    {
        if (StartDate.HasValue && EndDate.HasValue && StartDate.Value.Date > EndDate.Value.Date)
        {
            StatusMessage = "结束日期不能早于开始日期";
            return null;
        }

        var fromUtc = StartDate is { } start
            ? (DateTime?)ToUtcBoundary(start.Date)
            : null;
        var toUtc = EndDate is { } end
            ? (DateTime?)ToUtcBoundary(end.Date.AddDays(1))
            : null;

        return new AuditLogQuery(
            Actor: NormalizeActorFilter(ActorFilter),
            Action: SelectedAction?.Code,
            Text: TextFilter,
            FromUtcInclusive: fromUtc,
            ToUtcExclusive: toUtc,
            Limit: 200);
    }

    private static string? NormalizeActorFilter(string? value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return trimmed?.ToLowerInvariant() switch
        {
            "系统" => "system",
            "系统会话" => "session",
            "未知用户" => "unknown",
            _ => trimmed
        };
    }

    private static DateTime ToUtcBoundary(DateTime localDate)
    {
        var unspecified = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
    }

    private static DateTime ToLocalDateTime(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return utc.ToLocalTime();
    }
}
