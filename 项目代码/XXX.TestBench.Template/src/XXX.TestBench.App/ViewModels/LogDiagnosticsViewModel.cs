using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed class LogRow
{
    public required string Time { get; init; }
    public required string Actor { get; init; }
    public required string Action { get; init; }
    public required string Target { get; init; }
    public required string Detail { get; init; }
}

/// <summary>日志诊断：audit_logs 只读列表。</summary>
public sealed class LogDiagnosticsViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public LogDiagnosticsViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
    }

    public override string Title => "日志诊断";

    public ObservableCollection<LogRow> Logs { get; } = new();

    public RelayCommand LoadCommand { get; }

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
