using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 工艺监控点位表格中的一行，值与质量可被界面刷新。
/// </summary>
public sealed partial class PointRow : ObservableObject
{
    public required string Code { get; init; }
    public required string Address { get; init; }
    public string PointId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public required bool IsWritable { get; init; }
    public required WriteRiskLevel RiskLevel { get; init; }

    /// <summary>
    /// 点位当前的实时值文字。
    /// </summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>
    /// 点位当前的数据质量文字。
    /// </summary>
    [ObservableProperty]
    private string _quality = string.Empty;
}

/// <summary>
/// 工艺监控页面的兼容外壳。具体刷新、绑定和写入职责分拆在同名 partial 文件中。
/// </summary>
public sealed partial class ProcessMonitorViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;
    private readonly IProcessSnapshotReader _snapshotReader;

    public ProcessMonitorViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        _snapshotReader = new ProcessSnapshotReader();
        BuildProcessPointModels();
    }

    public override string Title => "工艺监控";

    /// <summary>
    /// 页面展示的点位列表。
    /// </summary>
    public ObservableCollection<PointRow> Points { get; } = new();

    /// <summary>
    /// 当前选中的点位。
    /// </summary>
    [ObservableProperty]
    private PointRow? _selectedPoint;

    partial void OnSelectedPointChanged(PointRow? value)
    {
        OnPropertyChanged(nameof(CanWriteSelectedPoint));
        WriteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 当前选中点位是否允许执行写入；高风险点位需要校准权限，普通点位需要手动控制权限。
    /// </summary>
    public bool CanWriteSelectedPoint => SelectedPoint is { IsWritable: true } point
        && _actor.HasPermission(point.RiskLevel == WriteRiskLevel.HighRisk
            ? PermissionCode.CalibrateDevices
            : PermissionCode.ManualControl);

    /// <summary>
    /// 操作员输入的要写入点位的值。
    /// </summary>
    [ObservableProperty]
    private string _writeValue = string.Empty;

    /// <summary>
    /// 高风险写入是否已经得到操作员确认。
    /// </summary>
    [ObservableProperty]
    private bool _riskConfirmed;
}
