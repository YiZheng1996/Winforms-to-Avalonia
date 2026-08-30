using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Input;

namespace XXX.TestBench.Avalonia.ViewModels;

/// <summary>
/// 只校验绝缘仪器参数草稿；没有平台适配器时不建立 TCP 连接，也不发送试验请求。
/// </summary>
public sealed class InstrumentViewModel : ObservableObject
{
    private string _ipAddress = "192.168.1.199";
    private int _port = 8888;
    private string _selectedTestType = "绝缘耐压";
    private double _withstandVoltage = 1000;
    private double _withstandSeconds = 10;
    private double _withstandLimit = 1;
    private double _insulationVoltage = 500;
    private double _insulationLimit = 100;
    private double _resistanceLimit = 100;
    private string _feedbackText = "可先校验仪器参数；连接与试验请求尚未接入平台服务。";

    public InstrumentViewModel()
    {
        TestTypes = new ReadOnlyCollection<string>(["绝缘耐压", "绝缘电阻", "电阻测试"]);
        ValidateDraftCommand = new RelayCommand(ValidateDraft);
        // 当前只迁移参数校验和状态展示。连接、发送、请求、结束与取消都可能触发
        // 真实仪器动作，因此在平台适配器和现场协议验收前由命令自身永久拒绝。
        ConnectCommand = new RelayCommand(() => { }, () => false);
        SendParametersCommand = new RelayCommand(() => { }, () => false);
        RequestTestCommand = new RelayCommand(() => { }, () => false);
        EndTestCommand = new RelayCommand(() => { }, () => false);
        CancelTestCommand = new RelayCommand(() => { }, () => false);
    }

    public IReadOnlyList<string> TestTypes { get; }
    public ICommand ValidateDraftCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand SendParametersCommand { get; }
    public ICommand RequestTestCommand { get; }
    public ICommand EndTestCommand { get; }
    public ICommand CancelTestCommand { get; }

    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public string SelectedTestType
    {
        get => _selectedTestType;
        set => SetProperty(ref _selectedTestType, value);
    }

    public double WithstandVoltage
    {
        get => _withstandVoltage;
        set => SetProperty(ref _withstandVoltage, value);
    }

    public double WithstandSeconds
    {
        get => _withstandSeconds;
        set => SetProperty(ref _withstandSeconds, value);
    }

    public double WithstandLimit
    {
        get => _withstandLimit;
        set => SetProperty(ref _withstandLimit, value);
    }

    public double InsulationVoltage
    {
        get => _insulationVoltage;
        set => SetProperty(ref _insulationVoltage, value);
    }

    public double InsulationLimit
    {
        get => _insulationLimit;
        set => SetProperty(ref _insulationLimit, value);
    }

    public double ResistanceLimit
    {
        get => _resistanceLimit;
        set => SetProperty(ref _resistanceLimit, value);
    }

    public string ConnectionStatusText => "未连接（适配器未实现）";
    public string FeedbackText => _feedbackText;
    public string SafetyBoundaryText =>
        "Legacy 仪器界面会建立 TCP 连接并发送试验参数。当前迁移只保留参数与状态界面，所有连接/发送/请求/结束/取消命令固定禁用。";
    public bool CanUseDeviceCommands => false;

    private void ValidateDraft()
    {
        var validEndpoint = IPAddress.TryParse(IpAddress, out _) && Port is > 0 and <= 65535;
        var validParameters = WithstandVoltage > 0
            && WithstandSeconds > 0
            && WithstandLimit >= 0
            && InsulationVoltage > 0
            && InsulationLimit >= 0
            && ResistanceLimit >= 0;
        _feedbackText = (validEndpoint, validParameters) switch
        {
            (true, true) => "参数格式校验通过；未连接仪器，也未发送任何命令。",
            (false, _) => "IP 地址或端口无效。",
            _ => "试验参数必须为合同允许的非负/正数。"
        };
        OnPropertyChanged(nameof(FeedbackText));
    }
}
