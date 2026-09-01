using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>运行总览：模式、设备健康、活动任务与快捷信息。</summary>
public sealed class OverviewViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public OverviewViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
    }

    public override string Title => "运行总览";

    public RelayCommand LoadCommand { get; }

    private string _deviceModeText = string.Empty;
    public string DeviceModeText { get => _deviceModeText; private set => SetField(ref _deviceModeText, value); }

    private string _healthText = string.Empty;
    public string HealthText { get => _healthText; private set => SetField(ref _healthText, value); }

    private string _activeTaskText = "无";
    public string ActiveTaskText { get => _activeTaskText; private set => SetField(ref _activeTaskText, value); }

    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            DeviceModeText = _services.DeviceConfig.DeviceMode == DeviceMode.Simulation ? "Simulation（仿真）" : "Hardware（硬件）";
            HealthText = _services.DeviceModes.Health.ToString();
            var running = await _services.TaskRepository.GetActiveRunningAsync(ct);
            ActiveTaskText = running is null ? "无" : $"{running.TaskNumber}（{running.State}）";
        }
        finally { IsBusy = false; }
    }
}
