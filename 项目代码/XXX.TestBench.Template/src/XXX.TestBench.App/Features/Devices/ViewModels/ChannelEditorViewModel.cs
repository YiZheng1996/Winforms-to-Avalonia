using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 通道编辑表单。只编辑候选对象，不接触串口、TCP 或运行时连接。
/// </summary>
public sealed partial class ChannelEditorViewModel : ObservableObject
{
    public ChannelEditorViewModel(
        ChannelEntry? current,
        IReadOnlyList<ChannelTransportChoice> transportOptions,
        string? defaultCode = null)
    {
        IsEdit = current is not null;
        var source = current ?? new ChannelEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = defaultCode ?? "CH_NEW",
            Name = "新通道",
            TransportKind = ChannelTransportKind.Tcp,
            TimeoutMs = 1000,
            RetryCount = 0,
            Tcp = new TcpChannelParameters()
        };

        TransportOptions = new ObservableCollection<ChannelTransportChoice>(transportOptions);
        var currentSerialPortName = source.Serial?.PortName.Trim() ?? string.Empty;
        SerialPortOptions = SerialPortOptionProvider.GetPortNames(currentSerialPortName);
        BaudRateOptions = SerialPortOptionProvider.GetBaudRates(source.Serial?.BaudRate);
        Id = source.Id;
        Code = source.Code;
        Name = source.Name;
        TransportKind = source.TransportKind == ChannelTransportKind.Unknown
            ? ChannelTransportKind.Tcp
            : source.TransportKind;
        TimeoutMsText = source.TimeoutMs.ToString(CultureInfo.InvariantCulture);
        RetryCountText = source.RetryCount.ToString(CultureInfo.InvariantCulture);
        SerialPortName = currentSerialPortName;
        SelectedBaudRate = source.Serial?.BaudRate > 0 ? source.Serial.BaudRate : null;
        DataBits = source.Serial?.DataBits ?? 8;
        SelectedParity = FindSerialSetting(ParityOptions, source.Serial?.Parity, "None");
        SelectedStopBits = FindSerialSetting(StopBitsOptions, source.Serial?.StopBits, "One");
        SimulationInstanceKey = source.Simulation?.InstanceKey ?? string.Empty;
        IsEnabled = source.Enabled;
        SelectedTransportOption = TransportOptions.FirstOrDefault(option => option.Value == TransportKind);
    }

    public string Id { get; }
    public bool IsEdit { get; }
    public string DialogTitle => IsEdit ? "编辑通道" : "新增通道";
    public string DialogSubtitle => IsEdit
        ? "修改连接资源，保存后返回设备点位管理"
        : "创建连接资源，完成后可在该通道下添加设备";
    public const int WizardStepCount = 4;
    public string CurrentStepText => $"第 {CurrentStep} 步，共 {WizardStepCount} 步";
    public string PrimaryActionText => CurrentStep == WizardStepCount
        ? (IsEdit ? "保存通道" : "创建通道")
        : "下一步";
    public string TransportSummaryText => SelectedTransportOption?.DisplayName
        ?? TransportKind switch
        {
            ChannelTransportKind.Tcp => "TCP 网络",
            ChannelTransportKind.Serial => "串口通信",
            ChannelTransportKind.Simulation => "离线仿真",
            _ => "未选择"
        };
    public bool CanGoBack => CurrentStep > 1;
    public bool CanProceed => CurrentStep < WizardStepCount || CanSave;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;
    public bool IsStep1Done => CurrentStep > 1;
    public bool IsStep2Done => CurrentStep > 2;
    public bool IsStep3Done => CurrentStep > 3;
    public bool IsStep1Pending => !IsStep1 && !IsStep1Done;
    public bool IsStep2Pending => !IsStep2 && !IsStep2Done;
    public bool IsStep3Pending => !IsStep3 && !IsStep3Done;
    public bool IsStep4Pending => !IsStep4;
    public ObservableCollection<ChannelTransportChoice> TransportOptions { get; }
    public IReadOnlyList<string> SerialPortOptions { get; }
    public IReadOnlyList<int> BaudRateOptions { get; }
    public IReadOnlyList<int> DataBitsOptions { get; } = [5, 6, 7, 8];
    public IReadOnlyList<SerialSettingChoice> ParityOptions { get; } =
    [
        new("None", "无校验"),
        new("Even", "偶校验"),
        new("Odd", "奇校验")
    ];
    public IReadOnlyList<SerialSettingChoice> StopBitsOptions { get; } =
    [
        new("One", "1 位"),
        new("Two", "2 位")
    ];

    public string Parity
    {
        get => SelectedParity?.Value ?? string.Empty;
        set => SelectedParity = string.IsNullOrWhiteSpace(value)
            ? null
            : FindSerialSetting(ParityOptions, value, "None");
    }

    public string StopBits
    {
        get => SelectedStopBits?.Value ?? string.Empty;
        set => SelectedStopBits = string.IsNullOrWhiteSpace(value)
            ? null
            : FindSerialSetting(StopBitsOptions, value, "One");
    }

    private int _currentStep = 1;
    public int CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (value is < 1 or > WizardStepCount || !SetProperty(ref _currentStep, value))
                return;

            OnPropertyChanged(nameof(CurrentStepText));
            OnPropertyChanged(nameof(PrimaryActionText));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanProceed));
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(IsStep4));
            OnPropertyChanged(nameof(IsStep1Done));
            OnPropertyChanged(nameof(IsStep2Done));
            OnPropertyChanged(nameof(IsStep3Done));
            OnPropertyChanged(nameof(IsStep1Pending));
            OnPropertyChanged(nameof(IsStep2Pending));
            OnPropertyChanged(nameof(IsStep3Pending));
            OnPropertyChanged(nameof(IsStep4Pending));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTcp))]
    [NotifyPropertyChangedFor(nameof(IsSerial))]
    [NotifyPropertyChangedFor(nameof(IsSimulation))]
    [NotifyPropertyChangedFor(nameof(TransportHelpText))]
    [NotifyPropertyChangedFor(nameof(TransportSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private ChannelTransportKind _transportKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransportSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private ChannelTransportChoice? _selectedTransportOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _timeoutMsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _retryCountText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _serialPortName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    [NotifyPropertyChangedFor(nameof(BaudRateText))]
    private int? _selectedBaudRate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private int _dataBits = 8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    [NotifyPropertyChangedFor(nameof(Parity))]
    private SerialSettingChoice? _selectedParity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    [NotifyPropertyChangedFor(nameof(StopBits))]
    private SerialSettingChoice? _selectedStopBits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _simulationInstanceKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    private bool _isEnabled = true;

    private string _validationMessage = string.Empty;
    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
                OnPropertyChanged(nameof(HasValidationMessage));
        }
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);
    public bool IsDisabled => !IsEnabled;

    public bool IsTcp => TransportKind == ChannelTransportKind.Tcp;
    public bool IsSerial => TransportKind == ChannelTransportKind.Serial;
    public bool IsSimulation => TransportKind == ChannelTransportKind.Simulation;
    public string BaudRateText => SelectedBaudRate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    public string TransportHelpText => TransportKind switch
    {
        ChannelTransportKind.Tcp => "TCP 通道只维护共享调度资源，可承载 S7 或 Modbus TCP；设备主机地址和端口分别配置。",
        ChannelTransportKind.Serial => "串口由通道独占，首版用于 Modbus RTU；应用前会拒绝两个启用通道占用同一个串口。",
        ChannelTransportKind.Simulation => "仿真通道只使用仿真实例标识，不会打开 TCP 或串口。",
        _ => "请选择一种传输类型。"
    };
    public bool CanSave => !string.IsNullOrWhiteSpace(Code)
        && !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(TimeoutMsText)
        && !string.IsNullOrWhiteSpace(RetryCountText)
        && SelectedTransportOption is not null
        && (IsTcp
            ? true
            : IsSerial
                ? !string.IsNullOrWhiteSpace(SerialPortName)
                    && SelectedBaudRate is > 0
                    && SelectedParity is not null
                    && SelectedStopBits is not null
                : IsSimulation && !string.IsNullOrWhiteSpace(SimulationInstanceKey));

    private static SerialSettingChoice FindSerialSetting(
        IReadOnlyList<SerialSettingChoice> options,
        string? configuredValue,
        string fallbackValue)
    {
        var normalized = configuredValue?.Trim();
        return options.FirstOrDefault(option =>
                   string.Equals(option.Value, normalized, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(option.DisplayName, normalized, StringComparison.Ordinal))
               ?? options.First(option => string.Equals(option.Value, fallbackValue, StringComparison.Ordinal));
    }

    /// <summary>
    /// 向导进入下一步前只校验当前步骤，避免用户在尚未看到的字段上被提前拦截。
    /// </summary>
    public bool MoveNext()
    {
        if (CurrentStep >= WizardStepCount)
            return false;

        if (!ValidateWizardStep(CurrentStep))
            return false;

        ValidationMessage = string.Empty;
        CurrentStep++;
        return true;
    }

    public void MoveBack()
    {
        if (CurrentStep <= 1)
            return;

        ValidationMessage = string.Empty;
        CurrentStep--;
    }

    private bool ValidateWizardStep(int step)
    {
        switch (step)
        {
            case 1 when SelectedTransportOption is null:
                ValidationMessage = "请选择连接方式";
                return false;
            case 2 when string.IsNullOrWhiteSpace(Name):
                ValidationMessage = "通道名称不能为空";
                return false;
            case 3:
                return TryBuild(out _);
            default:
                return true;
        }
    }

    partial void OnSelectedTransportOptionChanged(ChannelTransportChoice? value)
    {
        if (value is not null && TransportKind != value.Value)
            TransportKind = value.Value;
    }

    partial void OnTransportKindChanged(ChannelTransportKind value)
    {
        var option = TransportOptions.FirstOrDefault(item => item.Value == value);
        if (!ReferenceEquals(SelectedTransportOption, option))
            SelectedTransportOption = option;
    }

    public bool TryBuild(out ChannelEntry result)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code)) errors.Add("通道编码不能为空");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("通道名称不能为空");
        var timeout = ReadInt(TimeoutMsText, "超时时间", errors);
        var retryCount = ReadInt(RetryCountText, "重试次数", errors);
        var baudRate = ReadSelectedInt(SelectedBaudRate, "波特率", errors, required: IsSerial);

        result = new ChannelEntry
        {
            Id = Id,
            Code = Code.Trim(),
            Name = Name.Trim(),
            TransportKind = TransportKind,
            Enabled = IsEnabled,
            TimeoutMs = timeout,
            RetryCount = retryCount,
            Tcp = IsTcp ? new TcpChannelParameters() : null,
            Serial = IsSerial
                ? new SerialChannelParameters
                {
                    PortName = SerialPortName.Trim(),
                    BaudRate = baudRate,
                    DataBits = DataBits,
                    Parity = Parity.Trim(),
                    StopBits = StopBits.Trim()
                }
                : null,
            Simulation = IsSimulation
                ? new SimulationChannelParameters { InstanceKey = SimulationInstanceKey.Trim() }
                : null
        };

        errors.AddRange(result.Validate("channel")
            .Select(issue => issue.Message));
        ValidationMessage = string.Join("；", errors.Distinct(StringComparer.Ordinal));
        return errors.Count == 0;
    }

    internal void SetValidation(string message) => ValidationMessage = message;

    private static int ReadSelectedInt(int? value, string label, ICollection<string> errors, bool required = true)
    {
        if (value is null)
        {
            if (required) errors.Add($"{label}不能为空");
            return 0;
        }

        if (value <= 0)
        {
            errors.Add($"{label}必须大于 0");
            return 0;
        }

        return value.Value;
    }

    private static int ReadInt(string text, string label, ICollection<string> errors, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required) errors.Add($"{label}不能为空");
            return 0;
        }
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"{label}必须为整数");
            return 0;
        }
        return value;
    }
}

