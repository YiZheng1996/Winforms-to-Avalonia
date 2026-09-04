using System.Collections.ObjectModel;
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
/// 日志诊断页面：只读展示审计日志列表。
/// </summary>
public sealed partial class LogDiagnosticsViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public LogDiagnosticsViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "日志诊断";

    /// <summary>
    /// 页面展示的日志行集合。
    /// </summary>
    public ObservableCollection<LogRow> Logs { get; } = new();

    /// <summary>
    /// 页面加载命令：读取最近日志并刷新列表。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Logs.Clear();
            var entries = await ((IAuditLog)_services.AuditLog).ListRecentAsync(200, ct);
            foreach (var e in entries)
            {
                Logs.Add(new LogRow
                {
                    Time = e.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    Actor = LogTextLocalizer.Actor(e.Actor),
                    Action = LogTextLocalizer.Action(e.Action),
                    Target = LogTextLocalizer.Target(e.Action, e.Target),
                    Detail = LogTextLocalizer.Detail(e.Action, e.Detail)
                });
            }
        }
        finally { IsBusy = false; }
    }
}
