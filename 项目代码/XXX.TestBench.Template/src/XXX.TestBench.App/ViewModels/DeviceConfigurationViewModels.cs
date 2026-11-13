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
/// 设备级运行模式选项。模式只作用于当前设备，不再提供项目级统一开关。
/// </summary>
public sealed record DeviceModeChoice(DeviceMode Value, string DisplayName, string Description)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 通道传输类型的客户可读选项。传输类型和设备驱动仍是两个独立选择。
/// </summary>
public sealed record ChannelTransportChoice(
    ChannelTransportKind Value,
    string DisplayName,
    string Description)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 设备驱动的客户可读选项。未实现驱动只能用于仿真设备，不能用于硬件设备。
/// </summary>
public sealed record DeviceDriverChoice(
    string DriverKey,
    string DisplayName,
    bool IsImplemented,
    IReadOnlySet<DevicePointDataType> SupportedDataTypes,
    IReadOnlyList<DeviceModelDescriptor> DeviceModels,
    IReadOnlySet<ChannelTransportKind>? SupportedTransports = null)
{
    public string StatusText => IsImplemented ? "已实现" : "硬件模式不可应用";
    public string SummaryText => $"{DisplayName} · {StatusText}";
    public bool SupportsTransport(ChannelTransportKind transport)
        => SupportedTransports is null || SupportedTransports.Contains(transport);
    public override string ToString() => DisplayName;
}

/// <summary>
/// 设备驱动提供的系列选项。客户只选择，不再手工输入厂商、型号或 CPU Profile。
/// </summary>
public sealed record DeviceModelChoice(
    string Key,
    string DisplayName,
    string AddressWatermark,
    string AddressHint)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 设备扫描策略的客户可读选项。当前上位机只支持固定周期和明确按需读取。
/// </summary>
public sealed record DeviceScanModeChoice(
    DeviceScanMode Value,
    string DisplayName,
    string Description)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 独立通道编辑窗口提交的结果。
/// </summary>
public sealed record ChannelEditorDialogResult(ChannelEntry Entry);

/// <summary>
/// 独立设备编辑窗口提交的结果。
/// </summary>
public sealed record DeviceEditorDialogResult(DeviceConfig.DeviceEntry Entry);

/// <summary>
/// 设备编辑器中的通道引用选项。
/// </summary>
public sealed record ChannelReferenceChoice(
    string Id,
    string Code,
    string Name,
    ChannelTransportKind TransportKind,
    bool IsEnabled,
    int TimeoutMs = 0,
    int RetryCount = 0)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Code : Name;
    public string CodeText => string.IsNullOrWhiteSpace(Code) ? string.Empty : $"通道编码：{Code}";
    public string StatusText => IsEnabled ? "已启用" : "已停用";
    public string SummaryText => $"{TransportText} · {StatusText}";
    public string TransportText => TransportKind switch
    {
        ChannelTransportKind.Tcp => "TCP",
        ChannelTransportKind.Serial => "串口",
        ChannelTransportKind.Simulation => "仿真",
        _ => "未知"
    };
    public override string ToString() => DisplayName;
}

/// <summary>
/// 通道列表摘要行。
/// </summary>
public sealed class ChannelConfigurationRow
{
    public required ChannelEntry Entry { get; init; }
    public string Id => Entry.Id;
    public string Code => Entry.Code;
    public string Name => Entry.Name;
    public string CodeText => string.IsNullOrWhiteSpace(Code) ? string.Empty : $"通道编码：{Code}";
    public string TransportText => Entry.TransportKind switch
    {
        ChannelTransportKind.Tcp => "TCP",
        ChannelTransportKind.Serial => "串口",
        ChannelTransportKind.Simulation => "仿真",
        _ => "未知"
    };
    public string DetailsText => Entry.TransportKind switch
    {
        ChannelTransportKind.Tcp => "TCP 调度资源（设备端点独立配置）",
        ChannelTransportKind.Serial => $"{Entry.Serial?.PortName} · {Entry.Serial?.BaudRate}",
        ChannelTransportKind.Simulation => "仿真连接已配置",
        _ => "请检查传输参数"
    };
    public string EnabledText => Entry.Enabled ? "启用" : "停用";
}

/// <summary>
/// 设备列表摘要行。
/// </summary>
public sealed class DeviceConfigurationRow
{
    public required DeviceConfig.DeviceEntry Entry { get; init; }
    public required string ChannelText { get; init; }
    public required string DriverText { get; init; }
    public int PointCount { get; init; }
    public string Code => Entry.Code;
    public string Name => Entry.Name;
    public string CodeText => string.IsNullOrWhiteSpace(Code) ? string.Empty : $"设备编码：{Code}";
    public string DetailsText => $"{DriverText} · 通道：{ChannelText} · {PointCount} 个点位";
    public string ModeText => Entry.DeviceMode == DeviceMode.Simulation ? "仿真模式" : "硬件模式";
}

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
        TcpHost = source.Tcp?.Host ?? string.Empty;
        TcpPortText = source.Tcp?.Port.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SerialPortName = currentSerialPortName;
        SelectedBaudRate = source.Serial?.BaudRate > 0 ? source.Serial.BaudRate : null;
        DataBits = source.Serial?.DataBits ?? 8;
        Parity = source.Serial?.Parity ?? "None";
        StopBits = source.Serial?.StopBits ?? "One";
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
    public string CodeDisplayText => IsEdit ? Code : "系统自动生成";
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
    public IReadOnlyList<string> ParityOptions { get; } = ["None", "Even", "Odd", "Mark", "Space"];
    public IReadOnlyList<string> StopBitsOptions { get; } = ["One", "OnePointFive", "Two"];

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
    private string _tcpHost = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _tcpPortText = string.Empty;

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
    private string _parity = "None";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _stopBits = "One";

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
                    && !string.IsNullOrWhiteSpace(Parity)
                    && !string.IsNullOrWhiteSpace(StopBits)
                : IsSimulation && !string.IsNullOrWhiteSpace(SimulationInstanceKey));

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
        // v4 通道不再持有远端目标；TcpHost/TcpPortText 仅保留为旧对象反序列化兼容字段。
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

        errors.AddRange(result.Validate("channel", allowLegacyTargetEndpoint: false)
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

/// <summary>
/// 设备编辑表单。运行模式属于当前设备；设备创建后即参与运行，不再维护启用状态。
/// </summary>
public sealed partial class DeviceEditorViewModel : ObservableObject
{
    private readonly string _legacyProtocol;
    private readonly string _legacyAddress;
    private readonly IReadOnlyList<ChannelEntry> _channels;
    private IReadOnlyList<DeviceDriverChoice> _allDriverOptions = Array.Empty<DeviceDriverChoice>();
    private readonly SiemensS7ConnectionOptions? _originalSiemensS7;
    private readonly ModbusTcpConnectionOptions? _originalModbusTcp;
    private readonly IDeviceConnectionTester? _connectionTester;
    private readonly Func<bool>? _hasActiveRun;

    public DeviceEditorViewModel(
        DeviceConfig.DeviceEntry? current,
        IReadOnlyList<ChannelEntry> channels,
        IEnumerable<IDeviceDriverDescriptor>? descriptors,
        string? defaultCode = null,
        string? defaultChannelId = null,
        IDeviceConnectionTester? connectionTester = null,
        Func<bool>? hasActiveRun = null)
    {
        IsEdit = current is not null;
        IsChannelSelectionInherited = !IsEdit && !string.IsNullOrWhiteSpace(defaultChannelId);
        _channels = (channels ?? Array.Empty<ChannelEntry>()).Where(channel => channel is not null).ToList();
        _connectionTester = connectionTester;
        _hasActiveRun = hasActiveRun;
        var source = current ?? new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = defaultCode ?? "DEV_NEW",
            Name = "新设备",
            ChannelId = defaultChannelId ?? string.Empty,
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1500",
            DeviceMode = DeviceMode.Simulation,
            PollIntervalMs = 500,
            StaleAfterMs = 2000
        };

        Id = source.Id;
        _legacyProtocol = source.Protocol;
        _legacyAddress = source.Address;
        _originalSiemensS7 = source.SiemensS7 is null
            ? null
            : CloneSiemensS7(source.SiemensS7);
        _originalModbusTcp = source.ModbusTcp is null
            ? null
            : CloneModbusTcp(source.ModbusTcp);
        Code = source.Code;
        Name = source.Name;
        ModbusUnitIdText = source.ModbusUnitId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        PollIntervalMsText = source.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
        StaleAfterMsText = source.StaleAfterMs.ToString(CultureInfo.InvariantCulture);
        var legacyChannel = _channels.FirstOrDefault(channel =>
            string.Equals(channel.Id, source.ChannelId, StringComparison.OrdinalIgnoreCase));
        var s7 = source.SiemensS7;
        S7HostText = s7?.Host?.Trim()
            ?? (string.IsNullOrWhiteSpace(source.Address) ? legacyChannel?.Tcp?.Host?.Trim() : source.Address.Trim())
            ?? string.Empty;
        S7PortText = (s7?.Port is > 0 ? s7.Port : legacyChannel?.Tcp?.Port is > 0 ? legacyChannel.Tcp.Port : 102)
            .ToString(CultureInfo.InvariantCulture);
        S7RackText = (s7?.Rack ?? 0).ToString(CultureInfo.InvariantCulture);
        S7SlotText = (s7?.Slot ?? 0).ToString(CultureInfo.InvariantCulture);
        ModbusHostText = source.ModbusTcp?.Host?.Trim() ?? string.Empty;
        ModbusPortText = (source.ModbusTcp?.Port is > 0 ? source.ModbusTcp.Port : 502)
            .ToString(CultureInfo.InvariantCulture);
        ScanMode = source.ScanMode is DeviceScanMode.FixedInterval or DeviceScanMode.OnDemand
            ? source.ScanMode
            : DeviceScanMode.FixedInterval;
        SelectedScanMode = ScanModeOptions.FirstOrDefault(option => option.Value == ScanMode);
        var timing = source.Timing ?? new DeviceTimingOptions();
        ConnectTimeoutMsText = timing.ConnectTimeoutMs.ToString(CultureInfo.InvariantCulture);
        RequestTimeoutMsText = timing.RequestTimeoutMs.ToString(CultureInfo.InvariantCulture);
        RetryCountText = timing.RetryCount.ToString(CultureInfo.InvariantCulture);
        InterRequestDelayMsText = timing.InterRequestDelayMs.ToString(CultureInfo.InvariantCulture);
        var demotion = source.AutoDemotion ?? new DeviceDemotionOptions();
        DemotionEnabled = demotion.Enabled;
        FailureThresholdText = demotion.FailureThreshold.ToString(CultureInfo.InvariantCulture);
        DemotionPeriodMsText = demotion.DemotionPeriodMs.ToString(CultureInfo.InvariantCulture);
        SelectedDeviceMode = DeviceModeOptions.FirstOrDefault(option => option.Value == source.DeviceMode)
            ?? DeviceModeOptions.First();

        foreach (var channel in _channels)
            ChannelOptions.Add(new ChannelReferenceChoice(
                channel.Id, channel.Code, channel.Name, channel.TransportKind, channel.Enabled,
                channel.TimeoutMs, channel.RetryCount));

        _allDriverOptions = CreateDriverChoices(descriptors);
        foreach (var driver in _allDriverOptions)
            DriverOptions.Add(driver);

        SelectedChannel = ChannelOptions.FirstOrDefault(channel =>
            string.Equals(channel.Id, source.ChannelId, StringComparison.OrdinalIgnoreCase))
            ?? ChannelOptions.FirstOrDefault(channel => channel.IsEnabled)
            ?? ChannelOptions.FirstOrDefault();
        // 驱动下拉框必须服从通道类型：TCP 只显示 S7/Modbus TCP，串口只显示
        // Modbus RTU；Simulation 通道不是新增物理设备的承载通道。
        RefreshDriverOptions(source.DriverKey);
        SelectedDriver = DriverOptions.FirstOrDefault(driver =>
            string.Equals(driver.DriverKey, source.DriverKey, StringComparison.OrdinalIgnoreCase))
            ?? AddUnknownDriver(source.DriverKey);
        RefreshModelOptions(source.Model);
    }

    public string Id { get; }
    public bool IsEdit { get; }
    public string DialogTitle => IsEdit ? "编辑设备" : "新增设备";
    public string DialogSubtitle => IsEdit ? "修改设备归属和驱动参数" : "填写设备归属和驱动参数";
    public string DialogActionText => IsEdit ? "保存" : "新增";
    public bool IsChannelSelectionInherited { get; }
    public const int WizardStepCount = 3;
    public string CurrentStepText => $"第 {CurrentStep} 步，共 {WizardStepCount} 步";
    public string PrimaryActionText => CurrentStep switch
    {
        1 => "下一步：通信参数",
        2 => "下一步：确认保存",
        _ => IsEdit ? "保存设备" : "创建设备"
    };
    public bool CanGoBack => CurrentStep > 1;
    public bool CanProceed => CurrentStep < WizardStepCount || CanSave;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep1Done => CurrentStep > 1;
    public bool IsStep2Done => CurrentStep > 2;
    public bool IsStep3Pending => !IsStep3;
    public bool IsStep2Pending => !IsStep2 && !IsStep2Done;
    public bool IsStep1Pending => !IsStep1 && !IsStep1Done;
    public string SelectedDriverText => SelectedDriver?.DisplayName ?? "未选择";
    public string SelectedModelText => SelectedModel?.DisplayName ?? "未选择";
    public string SelectedChannelText => SelectedChannel?.DisplayName ?? "未选择";
    public string DeviceModeText => SelectedDeviceMode?.DisplayName ?? "未选择";
    public string DeviceModeSummaryText => DeviceModeText;
    public string PollIntervalSummaryText => FormatMilliseconds(PollIntervalMsText);
    public string StaleAfterSummaryText => FormatMilliseconds(StaleAfterMsText);
    public string S7EndpointSummaryText => IsS7Hardware
        ? $"{S7HostText.Trim()}:{S7PortText.Trim()} · 机架号 {S7RackText.Trim()} / 插槽号 {S7SlotText.Trim()}"
        : "不适用";
    public string ModbusEndpointSummaryText => IsModbusTcp && !IsSimulation
        ? $"{ModbusHostText.Trim()}:{ModbusPortText.Trim()} · 站号 {ModbusUnitIdText.Trim()}"
        : "不适用";
    public string ScanModeSummaryText => ScanMode == DeviceScanMode.OnDemand ? "按需读取" : "固定周期";
    public string TimingSummaryText => !IsSimulation
        ? $"请求 {RequestTimeoutMsText.Trim()} ms · 重试 {RetryCountText.Trim()} · 间隔 {InterRequestDelayMsText.Trim()} ms"
        : "不适用";
    public string DemotionSummaryText => !IsSimulation && DemotionEnabled
        ? $"启用 · {FailureThresholdText.Trim()} 次失败 · {DemotionPeriodMsText.Trim()} ms"
        : !IsSimulation ? "停用" : "不适用";
    public string ModbusUnitSummaryText => IsModbus && !string.IsNullOrWhiteSpace(ModbusUnitIdText)
        ? ModbusUnitIdText.Trim()
        : "不适用";
    public ObservableCollection<ChannelReferenceChoice> ChannelOptions { get; } = new();
    public ObservableCollection<DeviceDriverChoice> DriverOptions { get; } = new();
    public ObservableCollection<DeviceModelChoice> ModelOptions { get; } = new();
    public ObservableCollection<DeviceScanModeChoice> ScanModeOptions { get; } =
    [
        new(DeviceScanMode.FixedInterval, "固定周期", "由设备会话按固定周期后台采集并更新缓存"),
        new(DeviceScanMode.OnDemand, "按需读取", "只有明确的新鲜读取请求才访问设备")
    ];
    public ObservableCollection<DeviceModeChoice> DeviceModeOptions { get; } =
    [
        new(DeviceMode.Simulation, "仿真模式", "仅当前设备使用仿真，不连接现场设备"),
        new(DeviceMode.Hardware, "硬件模式", "仅当前设备按所选驱动连接真实设备")
    ];

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
            OnPropertyChanged(nameof(IsStep1Done));
            OnPropertyChanged(nameof(IsStep2Done));
            OnPropertyChanged(nameof(IsStep3Pending));
            OnPropertyChanged(nameof(IsStep2Pending));
            OnPropertyChanged(nameof(IsStep1Pending));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(BreadcrumbText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(BreadcrumbText))]
    [NotifyPropertyChangedFor(nameof(ChannelTimeoutText))]
    [NotifyPropertyChangedFor(nameof(ChannelRetryCountText))]
    [NotifyPropertyChangedFor(nameof(SelectedChannelText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    [NotifyPropertyChangedFor(nameof(CanTestConnection))]
    private ChannelReferenceChoice? _selectedChannel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimulation))]
    [NotifyPropertyChangedFor(nameof(IsModbus))]
    [NotifyPropertyChangedFor(nameof(IsModbusTcp))]
    [NotifyPropertyChangedFor(nameof(IsModbusRtu))]
    [NotifyPropertyChangedFor(nameof(IsS7))]
    [NotifyPropertyChangedFor(nameof(DriverHelpText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(SelectedDriverText))]
    [NotifyPropertyChangedFor(nameof(SelectedModelText))]
    [NotifyPropertyChangedFor(nameof(ModbusUnitSummaryText))]
    [NotifyPropertyChangedFor(nameof(ModbusEndpointSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private DeviceDriverChoice? _selectedDriver;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimulation))]
    [NotifyPropertyChangedFor(nameof(IsModbusTcpParametersVisible))]
    [NotifyPropertyChangedFor(nameof(IsRtuModbusParametersVisible))]
    [NotifyPropertyChangedFor(nameof(DeviceModeText))]
    [NotifyPropertyChangedFor(nameof(DeviceModeSummaryText))]
    [NotifyPropertyChangedFor(nameof(DeviceModeHelpText))]
    [NotifyPropertyChangedFor(nameof(DriverHelpText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private DeviceModeChoice? _selectedDeviceMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(SelectedModelText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private DeviceModelChoice? _selectedModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ModbusUnitSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _modbusUnitIdText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(PollIntervalSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _pollIntervalMsText = "500";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(StaleAfterSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _staleAfterMsText = "2000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(S7EndpointSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _s7HostText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(S7EndpointSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _s7PortText = "102";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(S7EndpointSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _s7RackText = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(S7EndpointSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _s7SlotText = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModbusEndpointSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _modbusHostText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModbusEndpointSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _modbusPortText = "502";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanModeSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private DeviceScanMode _scanMode = DeviceScanMode.FixedInterval;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanModeSummaryText))]
    private DeviceScanModeChoice? _selectedScanMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimingSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _connectTimeoutMsText = "3000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimingSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _requestTimeoutMsText = "1000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimingSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _retryCountText = "2";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimingSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _interRequestDelayMsText = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DemotionSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private bool _demotionEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DemotionSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _failureThresholdText = "3";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DemotionSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _demotionPeriodMsText = "10000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTestConnection))]
    private bool _isTestingConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionTestResult))]
    private string _connectionTestResultText = string.Empty;

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

    public bool IsSimulation => SelectedDeviceMode?.Value == DeviceMode.Simulation;
    public bool IsS7Hardware => IsS7 && !IsSimulation;
    public bool IsS7HardwareParametersVisible => IsS7Hardware;
    public bool IsModbusTcp => string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase);
    public bool IsModbusRtu => string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase);
    public bool IsModbusHardware => IsModbus && !IsSimulation;
    public bool IsHardware => !IsSimulation;
    public bool IsModbusTcpParametersVisible => IsModbusTcp && !IsSimulation;
    public bool IsRtuModbusParametersVisible => IsModbusRtu && !IsSimulation;
    public bool HardwareParametersVisible => IsHardware;
    public bool IsFixedIntervalScan => ScanMode == DeviceScanMode.FixedInterval;
    public bool IsOnDemandScan => ScanMode == DeviceScanMode.OnDemand;
    public bool CanTestConnection => IsHardware
        && !IsTestingConnection
        && _connectionTester is not null
        && SelectedDriver is { }
        && SelectedChannel is { } channel
        && SelectedDriver.SupportsTransport(channel.TransportKind);
    public bool HasConnectionTestResult => !string.IsNullOrWhiteSpace(ConnectionTestResultText);
    public string DeviceModeHelpText => SelectedDeviceMode?.Description
        ?? "模式只对当前设备生效。";
    public bool IsModbus => string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase)
        || string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase);
    public bool IsS7 => string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.SiemensS7, StringComparison.OrdinalIgnoreCase);
    public string DriverHelpText => SelectedDriver is null
        ? "请选择已注册驱动。"
        : SelectedChannel is { } channel && !SelectedDriver.SupportsTransport(channel.TransportKind)
            ? $"{SelectedDriver.DisplayName} 不能挂到当前{channel.TransportText}通道，请先切换通道。"
        : IsSimulation
            ? $"{SelectedDriver.DisplayName} 将按所选系列校验配置；当前设备为仿真模式，不连接现场设备。"
            : SelectedDriver.IsImplemented
                ? $"{SelectedDriver.DisplayName} 当前可用于硬件模式。"
                : $"{SelectedDriver.DisplayName} 的硬件通信尚未实现，不能在硬件模式使用。";
    public string BreadcrumbText => SelectedChannel is null
        ? "设备点位"
        : $"设备点位 / 通信通道：{SelectedChannel.DisplayName}";
    public string ChannelTimeoutText => SelectedChannel is { TimeoutMs: > 0 } channel
        ? channel.TimeoutMs.ToString(CultureInfo.InvariantCulture)
        : "由通道配置";
    public string ChannelRetryCountText => SelectedChannel is { } channel
        ? channel.RetryCount.ToString(CultureInfo.InvariantCulture)
        : "由通道配置";
    public bool CanSave
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Code)
                || string.IsNullOrWhiteSpace(Name)
                || SelectedChannel is null
                || SelectedDriver is null
                 || SelectedModel is null
                 || SelectedDeviceMode is null
                 || (!IsSimulation && !SelectedDriver.IsImplemented))
                return false;

            if (!SelectedDriver.SupportsTransport(SelectedChannel.TransportKind))
                return false;

            if (!int.TryParse(PollIntervalMsText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var poll)
                || poll <= 0
                || !int.TryParse(StaleAfterMsText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stale)
                || stale <= 0)
                return false;

            if (IsHardware && !IsHardwareFieldsValid(out _))
                return false;

            return !IsModbus
                || (int.TryParse(ModbusUnitIdText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unit)
                    && unit is >= 1 and <= 247);
        }
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
            case 1:
                if (string.IsNullOrWhiteSpace(Name))
                {
                    ValidationMessage = "设备名称不能为空";
                    return false;
                }
                if (SelectedDriver is null)
                {
                    ValidationMessage = "请选择设备驱动";
                    return false;
                }
                if (SelectedChannel is null)
                {
                    ValidationMessage = "请选择所属通道";
                    return false;
                }
                return true;
            case 2:
                return TryBuild(out _);
            default:
                return true;
        }
    }

    public bool TryBuild(out DeviceConfig.DeviceEntry result)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code)) errors.Add("设备编码不能为空");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("设备名称不能为空");
        if (SelectedChannel is null) errors.Add("请选择所属通道");
        if (SelectedDriver is null) errors.Add("请选择设备驱动");
        if (SelectedModel is null) errors.Add("请选择设备系列");
        if (SelectedDeviceMode is null) errors.Add("请选择当前设备运行模式");
        if (SelectedChannel is { } selectedChannel
            && SelectedDriver is { } selectedDriver
            && !selectedDriver.SupportsTransport(selectedChannel.TransportKind))
            errors.Add($"驱动“{selectedDriver.DisplayName}”不能使用当前通道传输类型：{selectedChannel.TransportKind}");
        if (!IsSimulation && SelectedDriver is { IsImplemented: false })
            errors.Add("该驱动的硬件通信尚未实现，不能在硬件模式使用");

        var poll = ReadPositiveInt(PollIntervalMsText, "轮询周期", errors);
        var stale = ReadPositiveInt(StaleAfterMsText, "数据陈旧判定时间", errors);
        SiemensS7ConnectionOptions? s7 = null;
        ModbusTcpConnectionOptions? modbusTcp = null;
        var timing = new DeviceTimingOptions();
        var demotion = new DeviceDemotionOptions
        {
            Enabled = DemotionEnabled,
            DiscardWritesWhileDemoted = true
        };
        if (IsS7Hardware)
        {
            var host = S7HostText.Trim();
            if (string.IsNullOrWhiteSpace(host)) errors.Add("PLC IP 不能为空");
            var port = ReadPort(S7PortText, errors);
            var rack = ReadS7NodeNumber(S7RackText, "机架号", errors);
            var slot = ReadS7NodeNumber(S7SlotText, "插槽号", errors);
            s7 = new SiemensS7ConnectionOptions { Host = host, Port = port, Rack = rack, Slot = slot };

            timing.ConnectTimeoutMs = ReadPositiveInt(ConnectTimeoutMsText, "连接超时", errors);
            timing.RequestTimeoutMs = ReadPositiveInt(RequestTimeoutMsText, "请求超时", errors);
            timing.RetryCount = ReadNonNegativeInt(RetryCountText, "读取重试次数", errors);
            timing.InterRequestDelayMs = ReadNonNegativeInt(InterRequestDelayMsText, "请求间隔", errors);
            demotion.FailureThreshold = ReadPositiveInt(FailureThresholdText, "连续失败阈值", errors);
            demotion.DemotionPeriodMs = ReadPositiveInt(DemotionPeriodMsText, "降级时长", errors);
            if (poll > 0 && timing.RequestTimeoutMs > 0
                && stale > 0 && stale < Math.Max(poll * 2, timing.RequestTimeoutMs + 100))
                errors.Add($"数据陈旧判定时间必须不小于 {Math.Max(poll * 2, timing.RequestTimeoutMs + 100)} ms");
        }
        if (IsModbusTcp && !IsSimulation)
        {
            var host = ModbusHostText.Trim();
            if (string.IsNullOrWhiteSpace(host)) errors.Add("Modbus TCP 地址不能为空");
            var port = ReadPort(ModbusPortText, errors, 502);
            modbusTcp = new ModbusTcpConnectionOptions { Host = host, Port = port };
        }
        else if (IsModbusTcp && _originalModbusTcp is not null)
        {
            modbusTcp = CloneModbusTcp(_originalModbusTcp);
        }
        if (IsHardware && !IsS7Hardware)
        {
            timing.ConnectTimeoutMs = ReadPositiveInt(ConnectTimeoutMsText, "连接超时", errors);
            timing.RequestTimeoutMs = ReadPositiveInt(RequestTimeoutMsText, "请求超时", errors);
            timing.RetryCount = ReadNonNegativeInt(RetryCountText, "读取重试次数", errors);
            timing.InterRequestDelayMs = ReadNonNegativeInt(InterRequestDelayMsText, "请求间隔", errors);
            demotion.FailureThreshold = ReadPositiveInt(FailureThresholdText, "连续失败阈值", errors);
            demotion.DemotionPeriodMs = ReadPositiveInt(DemotionPeriodMsText, "降级时长", errors);
            if (poll > 0 && timing.RequestTimeoutMs > 0
                && stale > 0 && stale < Math.Max(poll * 2, timing.RequestTimeoutMs + 100))
                errors.Add($"数据陈旧判定时间必须不小于 {Math.Max(poll * 2, timing.RequestTimeoutMs + 100)} ms");
            if (IsModbusRtu && SelectedChannel?.TransportKind != ChannelTransportKind.Serial)
                errors.Add("Modbus RTU 必须选择串口通道");
        }
        int? unitId = null;
        if (IsModbus)
        {
            if (string.IsNullOrWhiteSpace(ModbusUnitIdText))
                errors.Add("Modbus 设备必须填写站号");
            else if (!int.TryParse(ModbusUnitIdText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                errors.Add("Modbus 站号必须为整数");
            else if (parsed is < 1 or > 247)
                errors.Add("Modbus 站号必须在 1-247 范围内，首版不支持广播写");
            else
                unitId = parsed;
        }

        result = new DeviceConfig.DeviceEntry
        {
            Id = Id,
            Code = Code.Trim(),
            Name = Name.Trim(),
            DeviceMode = SelectedDeviceMode?.Value ?? DeviceMode.Simulation,
            Protocol = _legacyProtocol,
            Address = _legacyAddress,
            ChannelId = SelectedChannel?.Id ?? string.Empty,
            DriverKey = SelectedDriver?.DriverKey ?? string.Empty,
            Model = SelectedModel?.Key ?? string.Empty,
            ModbusUnitId = unitId,
            PollIntervalMs = poll,
            StaleAfterMs = stale,
            ScanMode = ScanMode,
            Timing = timing,
            AutoDemotion = demotion,
            SiemensS7 = s7 ?? (_originalSiemensS7 is null ? null : CloneSiemensS7(_originalSiemensS7)),
            ModbusTcp = modbusTcp
        };

        ValidationMessage = string.Join("；", errors.Distinct(StringComparer.Ordinal));
        return errors.Count == 0;
    }

    /// <summary>
    /// 只测试当前候选设备的连接，不保存配置、不读取点位、不切换 active revision。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        ConnectionTestResultText = string.Empty;
        if (_connectionTester is null)
        {
            ConnectionTestResultText = "连接测试服务未配置";
            return;
        }
        if (_hasActiveRun?.Invoke() == true)
        {
            ConnectionTestResultText = "存在活动试验，禁止测试连接";
            return;
        }
        if (!TryBuild(out var candidate))
        {
            ConnectionTestResultText = ValidationMessage;
            return;
        }
        var channel = _channels.FirstOrDefault(item =>
            string.Equals(item.Id, candidate.ChannelId, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
        {
            ConnectionTestResultText = "连接测试失败：所属通道不存在";
            return;
        }

        IsTestingConnection = true;
        TestConnectionCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _connectionTester.TestAsync(candidate, channel, ct);
            ConnectionTestResultText = DeviceConnectionTestResultFormatter.Format(result);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ConnectionTestResultText = DeviceConnectionTestResultFormatter.FormatTimeout();
        }
        catch (Exception ex)
        {
            ConnectionTestResultText = DeviceConnectionTestResultFormatter.FormatException(ex);
        }
        finally
        {
            IsTestingConnection = false;
            TestConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    internal void SetValidation(string message) => ValidationMessage = message;

    private static string FormatMilliseconds(string text)
        => string.IsNullOrWhiteSpace(text) ? "未设置" : $"{text.Trim()} ms";

    private DeviceDriverChoice? AddUnknownDriver(string driverKey)
    {
        if (string.IsNullOrWhiteSpace(driverKey)) return null;
        var choice = new DeviceDriverChoice(
            driverKey.Trim(),
            "未识别的设备驱动",
            false,
            new HashSet<DevicePointDataType>(),
            Array.Empty<DeviceModelDescriptor>());
        DriverOptions.Add(choice);
        return choice;
    }

    /// <summary>
    /// 根据当前通道过滤设备驱动选项。
    ///
    /// 通道是实际传输资源的唯一所有者，设备驱动只允许挂到自己声明的
    /// 传输类型上。切换通道后若旧驱动不再匹配，保留一个“不匹配”的
    /// 占位项用于编辑旧配置，但禁止保存，避免 UI 默默改协议。
    /// </summary>
    private void RefreshDriverOptions(string? preferredDriverKey)
    {
        var candidates = SelectedChannel is { } channel
            ? _allDriverOptions
                .Where(choice => choice.SupportsTransport(channel.TransportKind))
                .ToList()
            : _allDriverOptions.ToList();

        DriverOptions.Clear();
        foreach (var candidate in candidates)
            DriverOptions.Add(candidate);

        if (string.IsNullOrWhiteSpace(preferredDriverKey))
            return;

        var selected = DriverOptions.FirstOrDefault(option =>
            string.Equals(option.DriverKey, preferredDriverKey, StringComparison.OrdinalIgnoreCase))
            ?? AddUnknownDriver(preferredDriverKey);
        if (selected is not null && !ReferenceEquals(SelectedDriver, selected))
            SelectedDriver = selected;
    }

    private static int ReadPositiveInt(string text, string label, ICollection<string> errors)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"{label}必须为整数");
            return 0;
        }
        if (value <= 0) errors.Add($"{label}必须大于 0");
        return value;
    }

    private static int ReadNonNegativeInt(string text, string label, ICollection<string> errors)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"{label}必须为整数");
            return 0;
        }
        if (value < 0) errors.Add($"{label}不能小于 0");
        return value;
    }

    private static int ReadS7NodeNumber(string text, string label, ICollection<string> errors)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"{label}必须为整数");
            return 0;
        }
        if (value is < 0 or > short.MaxValue)
            errors.Add($"{label}必须在 0-{short.MaxValue} 范围内");
        return value;
    }

    private static int ReadPort(string text, ICollection<string> errors, int defaultPort = 102)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add("端口必须为整数");
            return defaultPort;
        }
        if (value is < 1 or > 65535) errors.Add("端口必须在 1-65535 范围内");
        return value;
    }

    private bool IsHardwareFieldsValid(out string error)
    {
        var errors = new List<string>();
        if (IsS7Hardware)
        {
            if (string.IsNullOrWhiteSpace(S7HostText)) errors.Add("PLC IP 不能为空");
            _ = ReadPort(S7PortText, errors);
            _ = ReadS7NodeNumber(S7RackText, "机架号", errors);
            _ = ReadS7NodeNumber(S7SlotText, "插槽号", errors);
        }
        if (IsModbusTcp)
        {
            if (string.IsNullOrWhiteSpace(ModbusHostText)) errors.Add("Modbus TCP 地址不能为空");
            _ = ReadPort(ModbusPortText, errors, 502);
        }
        if (IsModbusRtu && SelectedChannel?.TransportKind != ChannelTransportKind.Serial)
            errors.Add("Modbus RTU 必须选择串口通道");
        _ = ReadPositiveInt(ConnectTimeoutMsText, "连接超时", errors);
        var request = ReadPositiveInt(RequestTimeoutMsText, "请求超时", errors);
        _ = ReadNonNegativeInt(RetryCountText, "读取重试次数", errors);
        _ = ReadNonNegativeInt(InterRequestDelayMsText, "请求间隔", errors);
        var poll = int.TryParse(PollIntervalMsText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPoll)
            ? parsedPoll
            : 0;
        var stale = int.TryParse(StaleAfterMsText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStale)
            ? parsedStale
            : 0;
        if (poll > 0 && request > 0 && stale > 0 && stale < Math.Max(poll * 2, request + 100))
            errors.Add($"数据陈旧判定时间必须不小于 {Math.Max(poll * 2, request + 100)} ms");
        _ = ReadPositiveInt(FailureThresholdText, "连续失败阈值", errors);
        _ = ReadPositiveInt(DemotionPeriodMsText, "降级时长", errors);
        error = string.Join("；", errors.Distinct(StringComparer.Ordinal));
        return errors.Count == 0;
    }

    private static SiemensS7ConnectionOptions CloneSiemensS7(SiemensS7ConnectionOptions source)
        => new()
        {
            Host = source.Host,
            Port = source.Port,
            Rack = source.Rack,
            Slot = source.Slot
        };

    private static ModbusTcpConnectionOptions CloneModbusTcp(ModbusTcpConnectionOptions source)
        => new() { Host = source.Host, Port = source.Port };

    private static IReadOnlyList<DeviceDriverChoice> CreateDriverChoices(
        IEnumerable<IDeviceDriverDescriptor>? descriptors)
    {
        var choices = (descriptors ?? Array.Empty<IDeviceDriverDescriptor>())
            .Where(descriptor => descriptor is not null && !string.IsNullOrWhiteSpace(descriptor.DriverKey))
            .GroupBy(descriptor => descriptor.DriverKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var descriptor = group.First();
                return new DeviceDriverChoice(
                    descriptor.DriverKey.Trim(),
                    descriptor.DisplayName,
                    descriptor.IsImplemented,
                    descriptor.SupportedDataTypes,
                    descriptor.DeviceModels,
                    descriptor.SupportedTransports);
            })
            .Where(choice => !string.Equals(choice.DriverKey, DriverKeyCatalog.Simulation, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (choices.Count == 0)
        {
            choices.Add(new DeviceDriverChoice(
                DriverKeyCatalog.SiemensS7,
                "西门子 S7",
                false,
                new HashSet<DevicePointDataType>(),
                [new DeviceModelDescriptor("S7-1500", "S7-1500", "如：DB144.DBD88", "按 S7-1500 地址格式填写。" )],
                new HashSet<ChannelTransportKind> { ChannelTransportKind.Tcp }));
        }
        return choices;
    }

    partial void OnSelectedDriverChanged(DeviceDriverChoice? value)
    {
        RefreshModelOptions(null);
        OnPropertyChanged(nameof(IsSimulation));
        OnPropertyChanged(nameof(IsModbus));
        OnPropertyChanged(nameof(IsModbusTcp));
        OnPropertyChanged(nameof(IsModbusRtu));
        OnPropertyChanged(nameof(IsS7));
        OnPropertyChanged(nameof(IsS7Hardware));
        OnPropertyChanged(nameof(IsS7HardwareParametersVisible));
        OnPropertyChanged(nameof(IsModbusTcpParametersVisible));
        OnPropertyChanged(nameof(IsRtuModbusParametersVisible));
        OnPropertyChanged(nameof(DriverHelpText));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(S7EndpointSummaryText));
        OnPropertyChanged(nameof(ModbusEndpointSummaryText));
        OnPropertyChanged(nameof(TimingSummaryText));
        OnPropertyChanged(nameof(DemotionSummaryText));
        OnPropertyChanged(nameof(CanTestConnection));
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedChannelChanged(ChannelReferenceChoice? value)
    {
        RefreshDriverOptions(SelectedDriver?.DriverKey);
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(CanTestConnection));
        OnPropertyChanged(nameof(DriverHelpText));
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDeviceModeChanged(DeviceModeChoice? value)
    {
        OnPropertyChanged(nameof(IsSimulation));
        OnPropertyChanged(nameof(IsS7Hardware));
        OnPropertyChanged(nameof(IsS7HardwareParametersVisible));
        OnPropertyChanged(nameof(IsModbusTcpParametersVisible));
        OnPropertyChanged(nameof(IsRtuModbusParametersVisible));
        OnPropertyChanged(nameof(DeviceModeText));
        OnPropertyChanged(nameof(DeviceModeSummaryText));
        OnPropertyChanged(nameof(DriverHelpText));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(S7EndpointSummaryText));
        OnPropertyChanged(nameof(ModbusEndpointSummaryText));
        OnPropertyChanged(nameof(TimingSummaryText));
        OnPropertyChanged(nameof(DemotionSummaryText));
        OnPropertyChanged(nameof(CanTestConnection));
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnScanModeChanged(DeviceScanMode value)
    {
        var option = ScanModeOptions.FirstOrDefault(item => item.Value == value);
        if (!ReferenceEquals(SelectedScanMode, option))
            SelectedScanMode = option;
        OnPropertyChanged(nameof(ScanModeSummaryText));
        OnPropertyChanged(nameof(IsFixedIntervalScan));
        OnPropertyChanged(nameof(IsOnDemandScan));
    }

    partial void OnSelectedScanModeChanged(DeviceScanModeChoice? value)
    {
        if (value is not null && ScanMode != value.Value)
            ScanMode = value.Value;
        OnPropertyChanged(nameof(ScanModeSummaryText));
    }

    private void RefreshModelOptions(string? preferredModelKey)
    {
        ModelOptions.Clear();
        foreach (var model in SelectedDriver?.DeviceModels ?? Array.Empty<DeviceModelDescriptor>())
            ModelOptions.Add(new DeviceModelChoice(
                model.Key, model.DisplayName, model.AddressWatermark, model.AddressHint));

        SelectedModel = ModelOptions.FirstOrDefault(model =>
                string.Equals(model.Key, preferredModelKey, StringComparison.OrdinalIgnoreCase))
            ?? ModelOptions.FirstOrDefault();
    }
}

/// <summary>
/// 设备与通道配置编辑器。所有修改先保存在本地候选，保存时调用完整配置服务。
/// </summary>
public sealed partial class DeviceConfigurationEditorViewModel : ObservableObject
{
    private readonly DeviceConfigurationService _configurationService;
    private readonly UserContext _actor;
    private readonly List<PointsConfig.PointEntry> _points;
    private readonly SignalBindingsConfig _bindings;
    private readonly int _globalPollIntervalMs;
    private readonly int _globalTimeoutMs;
    private readonly IReadOnlyList<IDeviceDriverDescriptor> _descriptors;
    private readonly List<ChannelEntry> _channelEntries;
    private readonly List<DeviceConfig.DeviceEntry> _deviceEntries;
    private readonly IDeviceConnectionTester? _connectionTester;

    public DeviceConfigurationEditorViewModel(
        DeviceConfig current,
        IEnumerable<PointsConfig.PointEntry> points,
        SignalBindingsConfig? bindings,
        IEnumerable<IDeviceDriverDescriptor>? descriptors,
        DeviceConfigurationService configurationService,
        UserContext actor,
        IDeviceConnectionTester? connectionTester = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _points = (points ?? Array.Empty<PointsConfig.PointEntry>()).Where(point => point is not null).ToList();
        _bindings = bindings ?? new SignalBindingsConfig();
        _globalPollIntervalMs = current.PollIntervalMs;
        _globalTimeoutMs = current.TimeoutMs;
        _descriptors = (descriptors ?? Array.Empty<IDeviceDriverDescriptor>()).ToList();
        _connectionTester = connectionTester;
        _channelEntries = (current.Channels ?? new List<ChannelEntry>())
            .Where(channel => channel is not null)
            .Select(CloneChannel)
            .ToList();
        _deviceEntries = (current.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .Select(CloneDevice)
            .ToList();
        RefreshRows();
    }

    /// <summary>
    /// 由设备点位树把编辑器定位到指定通道，便于从树节点发起操作。
    /// </summary>
    public void SelectChannel(string channelId)
    {
        SelectedChannel = Channels.FirstOrDefault(channel =>
            string.Equals(channel.Id, channelId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 由设备点位树把编辑器定位到指定设备，便于从树节点发起操作。
    /// </summary>
    public void SelectDevice(string deviceId)
    {
        SelectedDevice = Devices.FirstOrDefault(device =>
            string.Equals(device.Entry.Id, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    public ObservableCollection<ChannelConfigurationRow> Channels { get; } = new();
    public ObservableCollection<DeviceConfigurationRow> Devices { get; } = new();
    public bool CanEdit => _actor.HasPermission(PermissionCode.ManageDevices);
    public string SummaryText => $"{Channels.Count} 个通道 · {Devices.Count} 个设备 · {_points.Count} 个点位；修改仅在点击“保存并应用”后生效。";
    public bool CanSave => CanEdit && !IsBusy && ChannelEditor is null && DeviceEditor is null;

    [ObservableProperty]
    private ChannelConfigurationRow? _selectedChannel;

    [ObservableProperty]
    private DeviceConfigurationRow? _selectedDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private ChannelEditorViewModel? _channelEditor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DeviceEditorViewModel? _deviceEditor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isBusy;

    private string _validationMessage = string.Empty;
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public void AddChannel()
        => CreateChannelEditor();

    public void EditSelectedChannel()
        => CreateChannelEditor(SelectedChannel?.Id);

    public bool DeleteSelectedChannel()
    {
        if (!CanEdit)
        {
            ValidationMessage = "当前用户没有设备管理权限";
            return false;
        }
        if (SelectedChannel is null)
        {
            ValidationMessage = "请先选择一个通道";
            return false;
        }
        var selected = SelectedChannel.Entry;
        if (_deviceEntries.Any(device => string.Equals(device.ChannelId, selected.Id, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = $"通道“{selected.Code}”仍被设备引用，请先修改或删除这些设备；系统不会级联删除。";
            return false;
        }
        _channelEntries.RemoveAll(channel => string.Equals(channel.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        RefreshRows();
        ValidationMessage = $"通道“{selected.Code}”已从候选中删除，保存并应用后才会生效。";
        return true;
    }

    public bool CommitChannelEditor()
    {
        if (ChannelEditor is null) return true;
        var editor = ChannelEditor;
        if (!editor.TryBuild(out var entry)) return false;
        return ApplyChannelEntry(entry);
    }

    /// <summary>
    /// 创建独立通道编辑窗口使用的表单。编码自动生成或沿用，不作为客户输入项。
    /// </summary>
    public ChannelEditorViewModel? CreateChannelEditor(string? channelId = null)
    {
        if (!CanEdit)
        {
            ValidationMessage = "当前用户没有设备管理权限";
            return null;
        }

        var current = string.IsNullOrWhiteSpace(channelId)
            ? null
            : _channelEntries.FirstOrDefault(channel =>
                string.Equals(channel.Id, channelId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(channelId) && current is null)
        {
            ValidationMessage = "找不到要编辑的通道";
            return null;
        }

        DeviceEditor = null;
        ChannelEditor = new ChannelEditorViewModel(
            current,
            TransportOptions,
            current is null
                ? NextCode("CH", _channelEntries.Select(channel => channel.Code))
                : null);
        ValidationMessage = string.Empty;
        return ChannelEditor;
    }

    /// <summary>
    /// 将独立通道编辑窗口的结果写入当前候选配置。
    /// </summary>
    public bool ApplyChannelEntry(ChannelEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // 无论调用方来自新编辑器还是旧配置恢复，候选 v4 配置都不再让通道持有 PLC 目标地址。
        var normalizedEntry = CloneChannel(entry);
        if (!CanEdit)
        {
            ValidationMessage = "当前用户没有设备管理权限";
            return false;
        }
        if (_channelEntries.Any(channel => !string.Equals(channel.Id, normalizedEntry.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(channel.Code, normalizedEntry.Code, StringComparison.OrdinalIgnoreCase)))
        {
            ChannelEditor?.SetValidation("通道编码已存在");
            ValidationMessage = "通道编码已存在";
            return false;
        }
        var index = _channelEntries.FindIndex(channel =>
            string.Equals(channel.Id, normalizedEntry.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) _channelEntries.Add(normalizedEntry);
        else _channelEntries[index] = normalizedEntry;
        ChannelEditor = null;
        RefreshRows();
        ValidationMessage = string.Empty;
        return true;
    }

    public void AddDevice()
        => CreateDeviceEditor(defaultChannelId: SelectedChannel?.Id);

    public void EditSelectedDevice()
        => CreateDeviceEditor(SelectedDevice?.Entry.Id);

    public bool DeleteSelectedDevice()
    {
        if (!CanEdit)
        {
            ValidationMessage = "当前用户没有设备管理权限";
            return false;
        }
        if (SelectedDevice is null)
        {
            ValidationMessage = "请先选择一个设备";
            return false;
        }
        var selected = SelectedDevice.Entry;
        var pointCount = _points.Count(point =>
            string.Equals(point.DeviceId, selected.Id, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(point.DeviceId)
                && string.Equals(point.DeviceCode, selected.Code, StringComparison.OrdinalIgnoreCase)));
        var bindingCount = (_bindings.Bindings ?? new Dictionary<string, string>())
            .Count(binding => string.Equals(binding.Value, selected.Id, StringComparison.OrdinalIgnoreCase));
        if (pointCount > 0 || bindingCount > 0)
        {
            ValidationMessage = $"设备“{selected.Code}”仍被 {pointCount} 个点位、{bindingCount} 个信号绑定引用，请先解除引用；系统不会静默删除。";
            return false;
        }
        _deviceEntries.RemoveAll(device => string.Equals(device.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        RefreshRows();
        ValidationMessage = $"设备“{selected.Code}”已从候选中删除，保存并应用后才会生效。";
        return true;
    }

    public bool CommitDeviceEditor()
    {
        if (DeviceEditor is null) return true;
        var editor = DeviceEditor;
        if (!editor.TryBuild(out var entry)) return false;
        return ApplyDeviceEntry(entry);
    }

    /// <summary>
    /// 创建独立设备编辑窗口使用的表单。编码自动生成或沿用，不作为客户输入项。
    /// </summary>
    public DeviceEditorViewModel? CreateDeviceEditor(
        string? deviceId = null,
        string? defaultChannelId = null)
    {
        if (!CanEdit)
        {
            ValidationMessage = "当前用户没有设备管理权限";
            return null;
        }
        if (_channelEntries.Count == 0)
        {
            ValidationMessage = "请先新增至少一个通道，再新增设备";
            return null;
        }

        var current = string.IsNullOrWhiteSpace(deviceId)
            ? null
            : _deviceEntries.FirstOrDefault(device =>
                string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(deviceId) && current is null)
        {
            ValidationMessage = "找不到要编辑的设备";
            return null;
        }

        ChannelEditor = null;
        DeviceEditor = new DeviceEditorViewModel(
            current,
            _channelEntries,
            _descriptors,
            current is null
                ? NextCode("DEV", _deviceEntries.Select(device => device.Code))
                : null,
            defaultChannelId,
            _connectionTester);
        ValidationMessage = string.Empty;
        return DeviceEditor;
    }

    /// <summary>
    /// 将独立设备编辑窗口的结果写入当前候选配置。
    /// </summary>
    public bool ApplyDeviceEntry(DeviceConfig.DeviceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanEdit)
        {
            ValidationMessage = "当前用户没有设备管理权限";
            return false;
        }
        if (_deviceEntries.Any(device => !string.Equals(device.Id, entry.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(device.Code, entry.Code, StringComparison.OrdinalIgnoreCase)))
        {
            DeviceEditor?.SetValidation("设备编码已存在");
            ValidationMessage = "设备编码已存在";
            return false;
        }
        var index = _deviceEntries.FindIndex(device =>
            string.Equals(device.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) _deviceEntries.Add(entry);
        else _deviceEntries[index] = entry;
        DeviceEditor = null;
        RefreshRows();
        ValidationMessage = string.Empty;
        return true;
    }

    public void CancelEditor()
    {
        ChannelEditor = null;
        DeviceEditor = null;
        ValidationMessage = string.Empty;
    }

    public async Task<DeviceConfigurationApplyResult> SaveAsync(
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        if (ChannelEditor is not null || DeviceEditor is not null)
            return Fail("请先完成或取消当前通道/设备编辑");
        if (!CanEdit)
            return Fail("当前用户没有设备管理权限");

        IsBusy = true;
        try
        {
            var result = await _configurationService.ApplyDeviceConfigurationAsync(
                _actor,
                new DeviceConfig
                {
                    SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                    PollIntervalMs = _globalPollIntervalMs,
                    TimeoutMs = _globalTimeoutMs,
                    Channels = _channelEntries.Select(CloneChannel).ToList(),
                    Devices = _deviceEntries.Select(CloneDevice).ToList()
                },
                ct,
                s7OptimizedBlockAccessConfirmed);
            ValidationMessage = result.Ok
                ? "设备配置已应用，运行状态已刷新"
                : result.Error ?? "设备配置应用失败";
            return result;
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
            return Fail(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private DeviceConfigurationApplyResult Fail(string message)
    {
        ValidationMessage = message;
        return new DeviceConfigurationApplyResult(false, string.Empty, message);
    }

    private void RefreshRows()
    {
        var selectedChannelId = SelectedChannel?.Id;
        var selectedDeviceId = SelectedDevice?.Entry.Id;
        Channels.Clear();
        foreach (var channel in _channelEntries)
            Channels.Add(new ChannelConfigurationRow { Entry = channel });
        Devices.Clear();
        foreach (var device in _deviceEntries)
        {
            var channel = _channelEntries.FirstOrDefault(item =>
                string.Equals(item.Id, device.ChannelId, StringComparison.OrdinalIgnoreCase));
            Devices.Add(new DeviceConfigurationRow
            {
                Entry = device,
                ChannelText = channel?.Code ?? "未找到通道",
                DriverText = DriverDisplayName(device.DriverKey),
                PointCount = _points.Count(point =>
                    string.Equals(point.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            });
        }
        SelectedChannel = Channels.FirstOrDefault(item =>
            string.Equals(item.Id, selectedChannelId, StringComparison.OrdinalIgnoreCase))
            ?? Channels.FirstOrDefault();
        SelectedDevice = Devices.FirstOrDefault(item =>
            string.Equals(item.Entry.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase))
            ?? Devices.FirstOrDefault();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanSave));
    }

    private string DriverDisplayName(string? driverKey)
    {
        var descriptor = _descriptors.FirstOrDefault(item =>
            string.Equals(item.DriverKey, driverKey, StringComparison.OrdinalIgnoreCase));
        return descriptor?.DisplayName ?? driverKey?.Trim() ?? "未知驱动";
    }

    private static IReadOnlyList<ChannelTransportChoice> TransportOptions { get; } =
    [
        new(ChannelTransportKind.Tcp, "TCP", "连接网络设备，目标地址在设备属性中配置"),
        new(ChannelTransportKind.Serial, "串口", "连接共享串行总线，填写串口参数")
    ];

    private static string NextCode(string prefix, IEnumerable<string> existing)
    {
        var used = existing.Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < 10000; index++)
        {
            var candidate = $"{prefix}_{index}";
            if (!used.Contains(candidate)) return candidate;
        }
        return prefix + "_NEW";
    }

    private static ChannelEntry CloneChannel(ChannelEntry source)
        => new()
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            TransportKind = source.TransportKind,
            Enabled = source.Enabled,
            TimeoutMs = source.TimeoutMs,
            RetryCount = source.RetryCount,
            Tcp = source.Tcp is null ? null : new TcpChannelParameters
            {
                Host = string.Empty,
                Port = 0,
                LocalInterface = source.Tcp.LocalInterface
            },
            Serial = source.Serial is null ? null : new SerialChannelParameters
            {
                PortName = source.Serial.PortName,
                BaudRate = source.Serial.BaudRate,
                DataBits = source.Serial.DataBits,
                Parity = source.Serial.Parity,
                StopBits = source.Serial.StopBits
            },
            Simulation = source.Simulation is null ? null : new SimulationChannelParameters { InstanceKey = source.Simulation.InstanceKey }
        };

    private static DeviceConfig.DeviceEntry CloneDevice(DeviceConfig.DeviceEntry source)
        => new()
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Protocol = source.Protocol,
            Address = source.Address,
            ChannelId = source.ChannelId,
            DriverKey = source.DriverKey,
            Model = source.Model,
            ModbusUnitId = source.ModbusUnitId,
            PollIntervalMs = source.PollIntervalMs,
            StaleAfterMs = source.StaleAfterMs,
            DeviceMode = source.DeviceMode,
            ScanMode = source.ScanMode,
            Timing = source.Timing is null ? new DeviceTimingOptions() : new DeviceTimingOptions
            {
                ConnectTimeoutMs = source.Timing.ConnectTimeoutMs,
                RequestTimeoutMs = source.Timing.RequestTimeoutMs,
                RetryCount = source.Timing.RetryCount,
                InterRequestDelayMs = source.Timing.InterRequestDelayMs
            },
            AutoDemotion = source.AutoDemotion is null ? new DeviceDemotionOptions() : new DeviceDemotionOptions
            {
                Enabled = source.AutoDemotion.Enabled,
                FailureThreshold = source.AutoDemotion.FailureThreshold,
                DemotionPeriodMs = source.AutoDemotion.DemotionPeriodMs,
                DiscardWritesWhileDemoted = source.AutoDemotion.DiscardWritesWhileDemoted
            },
            SiemensS7 = source.SiemensS7 is null ? null : new SiemensS7ConnectionOptions
            {
                Host = source.SiemensS7.Host,
                Port = source.SiemensS7.Port,
                Rack = source.SiemensS7.Rack,
                Slot = source.SiemensS7.Slot
            },
            ModbusTcp = source.ModbusTcp is null ? null : new ModbusTcpConnectionOptions
            {
                Host = source.ModbusTcp.Host,
                Port = source.ModbusTcp.Port
            }
        };
}
