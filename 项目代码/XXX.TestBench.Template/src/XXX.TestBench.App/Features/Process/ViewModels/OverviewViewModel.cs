using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 主界面实时测量行；当前模板值来自仿真展示数据，硬件阶段接入点表后替换。
/// </summary>
public sealed record MeasurementRowViewModel(string Name, string Unit, string Value, bool IsHealthy = true);

/// <summary>
/// 运行总览页面：展示设备模式、设备健康、活动试验记录与快捷测量信息。
/// </summary>
public sealed partial class OverviewViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public OverviewViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        Measurements =
        [
            new("MR(总风A)", "MPa", "0.72"),
            new("EX(缓解C)", "MPa", "0.71"),
            new("AC2(均衡E)", "MPa", "0.72"),
            new("BC(制动B)", "MPa", "0.73"),
            new("PE01", "MPa", "0.72"),
            new("PE02", "MPa", "0.72"),
            new("PE03", "MPa", "0.71"),
            new("PE04", "MPa", "0.71"),
            new("PE05", "MPa", "0.72"),
            new("PE06", "MPa", "0.71"),
            new("PE07", "MPa", "0.73"),
            new("PE08", "MPa", "0.72"),
            new("PE09", "MPa", "0.72")
        ];
    }

    public override string Title => "运行总览";

    /// <summary>
    /// 页面加载的测量值列表。
    /// </summary>
    public IReadOnlyList<MeasurementRowViewModel> Measurements { get; }

    private string _deviceModeText = string.Empty;

    /// <summary>
    /// 当前设备模式的显示文字。
    /// </summary>
    public string DeviceModeText { get => _deviceModeText; private set => SetProperty(ref _deviceModeText, value); }

    private string _healthText = string.Empty;

    /// <summary>
    /// 当前设备健康的显示文字。
    /// </summary>
    public string HealthText { get => _healthText; private set => SetProperty(ref _healthText, value); }

    private string _activeRecordText = "无";

    /// <summary>
    /// 当前活动试验记录的显示文字，无记录时显示“无”。
    /// </summary>
    public string ActiveRecordText { get => _activeRecordText; private set => SetProperty(ref _activeRecordText, value); }

    /// <summary>
    /// 页面加载命令：调用下方加载方法刷新数据。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            DeviceModeText = _services.DeviceModes.Runtime?.Mode switch
            {
                DeviceMode.Simulation => "仿真模式",
                DeviceMode.Hardware => "硬件模式",
                DeviceMode.Mixed => "按设备配置",
                _ => "按设备配置"
            };
            HealthText = _services.DeviceModes.Health.ToString();
            var running = await _services.RecordRepository.GetActiveRunningRecordAsync(ct);
            ActiveRecordText = running is null ? "无" : $"{running.RecordNumber}（{running.State}）";
        }
        finally { IsBusy = false; }
    }
}
