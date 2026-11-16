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
/// 设备编辑表单。运行模式属于当前设备；设备创建后即参与运行，不再维护启用状态。
/// </summary>
public sealed partial class DeviceEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<ChannelEntry> _channels;
    private IReadOnlyList<DeviceDriverChoice> _allDriverOptions = Array.Empty<DeviceDriverChoice>();
    private readonly SiemensS7ConnectionOptions? _originalSiemensS7;
    private readonly ModbusTcpConnectionOptions? _originalModbusTcp;
    private readonly IDeviceConnectionTester? _connectionTester;
    private bool _suppressModelDefaults;

    public DeviceEditorViewModel(
        DeviceConfig.DeviceEntry? current,
        IReadOnlyList<ChannelEntry> channels,
        IEnumerable<IDeviceDriverDescriptor>? descriptors,
        string? defaultCode = null,
        string? defaultChannelId = null,
        IDeviceConnectionTester? connectionTester = null)
    {
        _suppressModelDefaults = true;
        IsEdit = current is not null;
        IsChannelSelectionInherited = !IsEdit && !string.IsNullOrWhiteSpace(defaultChannelId);
        _channels = (channels ?? Array.Empty<ChannelEntry>()).Where(channel => channel is not null).ToList();
        _connectionTester = connectionTester;
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
        var s7 = source.SiemensS7;
        S7HostText = s7?.Host?.Trim() ?? string.Empty;
        S7PortText = (s7?.Port ?? 102).ToString(CultureInfo.InvariantCulture);
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
        InitializeModbusDataOrder(source.ModbusDataOrder, source.DriverKey);
        _suppressModelDefaults = false;
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
    public string ModbusDataOrderSummaryText => IsModbus
        ? SelectedModbusByteOrder is { } byteOrder && SelectedModbusWordOrder is { } wordOrder
            ? $"{byteOrder.DisplayName} · {wordOrder.DisplayName}"
            : "未配置"
        : "不适用";
    public string XinjeFieldV1DataOrderStatusText => !IsXinjeFieldV1
        ? string.Empty
        : SelectedModbusByteOrder?.Value == ByteOrder.BigEndian
            && SelectedModbusWordOrder?.Value == WordOrder.LowWordFirst
                ? "信捷现场验证配置 V1 固定使用：大端模式 · 低字在前。"
                : $"当前保存值：{ModbusDataOrderSummaryText}；该系列要求大端模式、低字在前。";
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
    public ObservableCollection<ModbusByteOrderChoice> ModbusByteOrderOptions { get; } =
    [
        new(ByteOrder.BigEndian, "大端模式", "每个 16 位寄存器按高字节在前解释"),
        new(ByteOrder.LittleEndian, "小端模式", "每个 16 位寄存器按低字节在前解释")
    ];
    public ObservableCollection<ModbusWordOrderChoice> ModbusWordOrderOptions { get; } =
    [
        new(WordOrder.HighWordFirst, "高字在前", "多寄存器值的高字寄存器在前"),
        new(WordOrder.LowWordFirst, "低字在前", "多寄存器值的低字寄存器在前")
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
    [NotifyPropertyChangedFor(nameof(IsXinjeFieldV1))]
    [NotifyPropertyChangedFor(nameof(IsGenericModbus))]
    [NotifyPropertyChangedFor(nameof(XinjeFieldV1DataOrderStatusText))]
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
    [NotifyPropertyChangedFor(nameof(IsXinjeFieldV1))]
    [NotifyPropertyChangedFor(nameof(IsGenericModbus))]
    [NotifyPropertyChangedFor(nameof(XinjeFieldV1DataOrderStatusText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private DeviceModelChoice? _selectedModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ModbusUnitSummaryText))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private string _modbusUnitIdText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModbusDataOrderSummaryText))]
    [NotifyPropertyChangedFor(nameof(XinjeFieldV1DataOrderStatusText))]
    [NotifyPropertyChangedFor(nameof(CanRestoreXinjeFieldV1DataOrder))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private ModbusByteOrderChoice? _selectedModbusByteOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModbusDataOrderSummaryText))]
    [NotifyPropertyChangedFor(nameof(XinjeFieldV1DataOrderStatusText))]
    [NotifyPropertyChangedFor(nameof(CanRestoreXinjeFieldV1DataOrder))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    private ModbusWordOrderChoice? _selectedModbusWordOrder;

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
    public bool IsXinjeFieldV1 => IsModbusTcp
        && string.Equals(SelectedModel?.Key, DeviceModelKeyCatalog.XinjeFieldV1, StringComparison.OrdinalIgnoreCase);
    public bool IsGenericModbus => IsModbus && !IsXinjeFieldV1;
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
                    && unit is >= 1 and <= 247
                    && SelectedModbusByteOrder is not null
                    && SelectedModbusWordOrder is not null
                    && (!IsXinjeFieldV1
                        || (SelectedModbusByteOrder.Value == ByteOrder.BigEndian
                            && SelectedModbusWordOrder.Value == WordOrder.LowWordFirst)));
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
            Enabled = DemotionEnabled
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
        ModbusDataOrderOptions? modbusDataOrder = null;
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

            if (SelectedModbusByteOrder is null)
                errors.Add("Modbus 设备必须选择大端模式或小端模式");
            if (SelectedModbusWordOrder is null)
                errors.Add("Modbus 设备必须选择高字在前或低字在前");
            if (SelectedModbusByteOrder is { } byteOrder
                && SelectedModbusWordOrder is { } wordOrder)
            {
                modbusDataOrder = new ModbusDataOrderOptions
                {
                    ByteOrder = byteOrder.Value,
                    WordOrder = wordOrder.Value
                };
            }
            if (IsXinjeFieldV1
                && (SelectedModbusByteOrder?.Value != ByteOrder.BigEndian
                    || SelectedModbusWordOrder?.Value != WordOrder.LowWordFirst))
                errors.Add("信捷 PLC（现场验证配置 V1）固定使用大端模式、低字在前");
        }

        result = new DeviceConfig.DeviceEntry
        {
            Id = Id,
            Code = Code.Trim(),
            Name = Name.Trim(),
            DeviceMode = SelectedDeviceMode?.Value ?? DeviceMode.Simulation,
            ChannelId = SelectedChannel?.Id ?? string.Empty,
            DriverKey = SelectedDriver?.DriverKey ?? string.Empty,
            Model = SelectedModel?.Key ?? string.Empty,
            ModbusUnitId = unitId,
            ModbusDataOrder = modbusDataOrder,
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

    private void InitializeModbusDataOrder(
        ModbusDataOrderOptions? source,
        string? driverKey)
    {
        if (!IsModbusDriver(driverKey))
        {
            SelectedModbusByteOrder = null;
            SelectedModbusWordOrder = null;
            return;
        }

        SelectedModbusByteOrder = source is null
            ? null
            : ModbusByteOrderOptions.FirstOrDefault(option => option.Value == source.ByteOrder);
        SelectedModbusWordOrder = source is null
            ? null
            : ModbusWordOrderOptions.FirstOrDefault(option => option.Value == source.WordOrder);
    }

    private static bool IsModbusDriver(string? driverKey)
        => string.Equals(driverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase)
            || string.Equals(driverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase);

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
        if (IsModbusDriver(value?.DriverKey))
        {
            SelectedModbusByteOrder ??= ModbusByteOrderOptions.First();
            SelectedModbusWordOrder ??= ModbusWordOrderOptions.First();
        }
        else
        {
            SelectedModbusByteOrder = null;
            SelectedModbusWordOrder = null;
        }
        RefreshModelOptions(null);
        OnPropertyChanged(nameof(IsSimulation));
        OnPropertyChanged(nameof(IsModbus));
        OnPropertyChanged(nameof(IsModbusTcp));
        OnPropertyChanged(nameof(IsModbusRtu));
        OnPropertyChanged(nameof(IsXinjeFieldV1));
        OnPropertyChanged(nameof(IsGenericModbus));
        OnPropertyChanged(nameof(XinjeFieldV1DataOrderStatusText));
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
        OnPropertyChanged(nameof(ModbusDataOrderSummaryText));
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

    partial void OnSelectedModbusByteOrderChanged(ModbusByteOrderChoice? value)
    {
        OnPropertyChanged(nameof(ModbusDataOrderSummaryText));
        OnPropertyChanged(nameof(XinjeFieldV1DataOrderStatusText));
        OnPropertyChanged(nameof(CanRestoreXinjeFieldV1DataOrder));
        RestoreXinjeFieldV1DataOrderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanProceed));
    }

    partial void OnSelectedModbusWordOrderChanged(ModbusWordOrderChoice? value)
    {
        OnPropertyChanged(nameof(ModbusDataOrderSummaryText));
        OnPropertyChanged(nameof(XinjeFieldV1DataOrderStatusText));
        OnPropertyChanged(nameof(CanRestoreXinjeFieldV1DataOrder));
        RestoreXinjeFieldV1DataOrderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanProceed));
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

    partial void OnSelectedModelChanged(DeviceModelChoice? value)
    {
        if (!_suppressModelDefaults && IsXinjeFieldV1)
        {
            // 只有用户主动选择该系列时才建议固定参数；编辑回填由初始化保护拦截。
            if (string.IsNullOrWhiteSpace(ModbusUnitIdText))
                ModbusUnitIdText = "1";
            if (string.IsNullOrWhiteSpace(ModbusPortText))
                ModbusPortText = "502";
            SelectedModbusByteOrder = ModbusByteOrderOptions.First(option => option.Value == ByteOrder.BigEndian);
            SelectedModbusWordOrder = ModbusWordOrderOptions.First(option => option.Value == WordOrder.LowWordFirst);
        }

        OnPropertyChanged(nameof(IsGenericModbus));
        OnPropertyChanged(nameof(XinjeFieldV1DataOrderStatusText));
        OnPropertyChanged(nameof(CanRestoreXinjeFieldV1DataOrder));
        RestoreXinjeFieldV1DataOrderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanProceed));
    }

    public bool CanRestoreXinjeFieldV1DataOrder => IsXinjeFieldV1
        && (SelectedModbusByteOrder?.Value != ByteOrder.BigEndian
            || SelectedModbusWordOrder?.Value != WordOrder.LowWordFirst);

    [RelayCommand(CanExecute = nameof(CanRestoreXinjeFieldV1DataOrder))]
    public void RestoreXinjeFieldV1DataOrder()
    {
        SelectedModbusByteOrder = ModbusByteOrderOptions.First(option => option.Value == ByteOrder.BigEndian);
        SelectedModbusWordOrder = ModbusWordOrderOptions.First(option => option.Value == WordOrder.LowWordFirst);
    }
}

