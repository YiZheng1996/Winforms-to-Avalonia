using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 日志表格中的一行。
/// </summary>
public sealed class LogRow
{
    public required string Time { get; init; }
    public required string Actor { get; init; }
    public required string Action { get; init; }
    public required string Target { get; init; }
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
                Logs.Add(new LogRow { Time = e.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"), Actor = e.Actor, Action = e.Action, Target = e.Target ?? string.Empty, Detail = e.Detail ?? string.Empty });
        }
        finally { IsBusy = false; }
    }
}