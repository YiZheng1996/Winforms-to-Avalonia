using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 应用外壳状态：系统名称、设备模式、连接状态、当前用户、底部状态与故障诊断。
/// 业务页面在阶段 1 规格锁定前不加入。
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private string _deviceModeText = string.Empty;
    private string _connectionStatusText = string.Empty;
    private string _currentUserText = string.Empty;
    private string _bottomStatusText = string.Empty;
    private bool _isFaulted;
    private string? _faultMessage;

    public ShellViewModel(
        string systemName,
        string version,
        DeviceMode deviceMode,
        string databasePath,
        bool isFaulted = false,
        string? faultMessage = null,
        string? deviceError = null)
    {
        SystemName = systemName;
        Version = version;
        DatabasePath = databasePath;
        IsFaulted = isFaulted;
        FaultMessage = faultMessage;

        DeviceModeText = deviceMode == DeviceMode.Simulation ? "Simulation（仿真）" : "Hardware（硬件）";
        IsSimulationMode = deviceMode == DeviceMode.Simulation;
        ConnectionStatusText = isFaulted ? "故障" : (deviceError ?? "运行正常");
        CurrentUserText = "未登录";
        BottomStatusText = $"数据库：{databasePath}  设备：{DeviceModeText}  版本：{version}";
    }

    public string SystemName { get; }
    public string Version { get; }
    public string DatabasePath { get; }
    public bool IsSimulationMode { get; }

    public string DeviceModeText
    {
        get => _deviceModeText;
        private set => SetField(ref _deviceModeText, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetField(ref _connectionStatusText, value);
    }

    public string CurrentUserText
    {
        get => _currentUserText;
        private set => SetField(ref _currentUserText, value);
    }

    public string BottomStatusText
    {
        get => _bottomStatusText;
        private set => SetField(ref _bottomStatusText, value);
    }

    public bool IsFaulted
    {
        get => _isFaulted;
        private set => SetField(ref _isFaulted, value);
    }

    public string? FaultMessage
    {
        get => _faultMessage;
        private set => SetField(ref _faultMessage, value);
    }
}
