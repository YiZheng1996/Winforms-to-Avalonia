using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

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
/// 设备驱动的客户可读选项。未实现驱动可以进入禁用草稿，但不能启用应用。
/// </summary>
public sealed record DeviceDriverChoice(
    string DriverKey,
    string DisplayName,
    bool IsImplemented,
    IReadOnlySet<DevicePointDataType> SupportedDataTypes)
{
    public string StatusText => IsImplemented ? "已实现" : "未实现，启用前不可应用";
    public string SummaryText => $"{DriverKey} · {StatusText}";
    public override string ToString() => DisplayName;
}

/// <summary>
/// 设备编辑器中的通道引用选项。
/// </summary>
public sealed record ChannelReferenceChoice(
    string Id,
    string Code,
    string Name,
    ChannelTransportKind TransportKind,
    bool IsEnabled)
{
    public string DisplayName => $"{Code}  {Name}";
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
    public string TransportText => Entry.TransportKind switch
    {
        ChannelTransportKind.Tcp => "TCP",
        ChannelTransportKind.Serial => "串口",
        ChannelTransportKind.Simulation => "仿真",
        _ => "未知"
    };
    public string DetailsText => Entry.TransportKind switch
    {
        ChannelTransportKind.Tcp => $"{Entry.Tcp?.Host}:{Entry.Tcp?.Port}",
        ChannelTransportKind.Serial => $"{Entry.Serial?.PortName} · {Entry.Serial?.BaudRate}",
        ChannelTransportKind.Simulation => Entry.Simulation?.InstanceKey ?? "未设置实例",
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
    public string DetailsText => $"{DriverText} · 通道：{ChannelText} · {PointCount} 个点位";
    public string EnabledText => Entry.Enabled ? "启用" : "停用";
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
        var source = current ?? new ChannelEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = defaultCode ?? "CH_NEW",
            Name = "新通道",
            TransportKind = ChannelTransportKind.Simulation,
            TimeoutMs = 1000,
            RetryCount = 0,
            Simulation = new SimulationChannelParameters { InstanceKey = "simulation" }
        };

        TransportOptions = new ObservableCollection<ChannelTransportChoice>(transportOptions);
        Id = source.Id;
        Code = source.Code;
        Name = source.Name;
        TransportKind = source.TransportKind == ChannelTransportKind.Unknown
            ? ChannelTransportKind.Simulation
            : source.TransportKind;
        TimeoutMsText = source.TimeoutMs.ToString(CultureInfo.InvariantCulture);
        RetryCountText = source.RetryCount.ToString(CultureInfo.InvariantCulture);
        TcpHost = source.Tcp?.Host ?? string.Empty;
        TcpPortText = source.Tcp?.Port.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SerialPortName = source.Serial?.PortName ?? string.Empty;
        BaudRateText = source.Serial?.BaudRate.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        DataBits = source.Serial?.DataBits ?? 8;
        Parity = source.Serial?.Parity ?? "None";
        StopBits = source.Serial?.StopBits ?? "One";
        SimulationInstanceKey = source.Simulation?.InstanceKey ?? string.Empty;
        IsEnabled = source.Enabled;
        SelectedTransportOption = TransportOptions.FirstOrDefault(option => option.Value == TransportKind);
    }

    public string Id { get; }
    public ObservableCollection<ChannelTransportChoice> TransportOptions { get; }
    public IReadOnlyList<int> DataBitsOptions { get; } = [5, 6, 7, 8];
    public IReadOnlyList<string> ParityOptions { get; } = ["None", "Even", "Odd", "Mark", "Space"];
    public IReadOnlyList<string> StopBitsOptions { get; } = ["One", "OnePointFive", "Two"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTcp))]
    [NotifyPropertyChangedFor(nameof(IsSerial))]
    [NotifyPropertyChangedFor(nameof(IsSimulation))]
    [NotifyPropertyChangedFor(nameof(TransportHelpText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private ChannelTransportKind _transportKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private ChannelTransportChoice? _selectedTransportOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _timeoutMsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _retryCountText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _tcpHost = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _tcpPortText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _serialPortName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _baudRateText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private int _dataBits = 8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _parity = "None";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _stopBits = "One";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _simulationInstanceKey = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    private string _validationMessage = string.Empty;
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool IsTcp => TransportKind == ChannelTransportKind.Tcp;
    public bool IsSerial => TransportKind == ChannelTransportKind.Serial;
    public bool IsSimulation => TransportKind == ChannelTransportKind.Simulation;
    public string TransportHelpText => TransportKind switch
    {
        ChannelTransportKind.Tcp => "TCP 通道由 Host 和 Port 定位；同一通道内的设备共享一个请求调度器。",
        ChannelTransportKind.Serial => "串口由通道独占；应用前会拒绝两个启用通道占用同一个串口。",
        ChannelTransportKind.Simulation => "仿真通道只使用 InstanceKey，不会打开 TCP 或串口。",
        _ => "请选择一种传输类型。"
    };
    public bool CanSave => !string.IsNullOrWhiteSpace(Code)
        && !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(TimeoutMsText)
        && !string.IsNullOrWhiteSpace(RetryCountText);

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
        var tcpPort = ReadInt(TcpPortText, "TCP 端口", errors, required: IsTcp);
        var baudRate = ReadInt(BaudRateText, "波特率", errors, required: IsSerial);

        result = new ChannelEntry
        {
            Id = Id,
            Code = Code.Trim(),
            Name = Name.Trim(),
            TransportKind = TransportKind,
            Enabled = IsEnabled,
            TimeoutMs = timeout,
            RetryCount = retryCount,
            Tcp = IsTcp ? new TcpChannelParameters { Host = TcpHost.Trim(), Port = tcpPort } : null,
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

        errors.AddRange(result.Validate("channel").Select(issue => issue.Message));
        ValidationMessage = string.Join("；", errors.Distinct(StringComparer.Ordinal));
        return errors.Count == 0;
    }

    internal void SetValidation(string message) => ValidationMessage = message;

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
/// 设备编辑表单。驱动未实现时允许保留为停用草稿，但不会让它成为可运行设备。
/// </summary>
public sealed partial class DeviceEditorViewModel : ObservableObject
{
    private readonly string _legacyProtocol;
    private readonly string _legacyAddress;

    public DeviceEditorViewModel(
        DeviceConfig.DeviceEntry? current,
        IReadOnlyList<ChannelEntry> channels,
        IEnumerable<IDeviceDriverDescriptor>? descriptors,
        string? defaultCode = null,
        string? defaultChannelId = null)
    {
        var source = current ?? new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = defaultCode ?? "DEV_NEW",
            Name = "新设备",
            ChannelId = defaultChannelId ?? string.Empty,
            DriverKey = DriverKeyCatalog.Simulation,
            PollIntervalMs = 500,
            StaleAfterMs = 2000,
            Enabled = true
        };

        Id = source.Id;
        _legacyProtocol = source.Protocol;
        _legacyAddress = source.Address;
        Code = source.Code;
        Name = source.Name;
        Manufacturer = source.Manufacturer;
        Model = source.Model;
        CpuProfile = source.CpuProfile;
        ModbusUnitIdText = source.ModbusUnitId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        PollIntervalMsText = source.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
        StaleAfterMsText = source.StaleAfterMs.ToString(CultureInfo.InvariantCulture);
        IsEnabled = source.Enabled;

        foreach (var channel in channels.Where(channel => channel is not null))
            ChannelOptions.Add(new ChannelReferenceChoice(
                channel.Id, channel.Code, channel.Name, channel.TransportKind, channel.Enabled));

        foreach (var driver in CreateDriverChoices(descriptors))
            DriverOptions.Add(driver);

        SelectedChannel = ChannelOptions.FirstOrDefault(channel =>
            string.Equals(channel.Id, source.ChannelId, StringComparison.OrdinalIgnoreCase))
            ?? ChannelOptions.FirstOrDefault(channel => channel.IsEnabled)
            ?? ChannelOptions.FirstOrDefault();
        SelectedDriver = DriverOptions.FirstOrDefault(driver =>
            string.Equals(driver.DriverKey, source.DriverKey, StringComparison.OrdinalIgnoreCase))
            ?? AddUnknownDriver(source.DriverKey);
    }

    public string Id { get; }
    public ObservableCollection<ChannelReferenceChoice> ChannelOptions { get; } = new();
    public ObservableCollection<DeviceDriverChoice> DriverOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private ChannelReferenceChoice? _selectedChannel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimulation))]
    [NotifyPropertyChangedFor(nameof(IsModbus))]
    [NotifyPropertyChangedFor(nameof(IsS7))]
    [NotifyPropertyChangedFor(nameof(DriverHelpText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DeviceDriverChoice? _selectedDriver;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _cpuProfile = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _modbusUnitIdText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _pollIntervalMsText = "500";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _staleAfterMsText = "2000";

    [ObservableProperty]
    private bool _isEnabled = true;

    private string _validationMessage = string.Empty;
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool IsSimulation => string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.Simulation, StringComparison.OrdinalIgnoreCase);
    public bool IsModbus => string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase)
        || string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase);
    public bool IsS7 => string.Equals(SelectedDriver?.DriverKey, DriverKeyCatalog.SiemensS7, StringComparison.OrdinalIgnoreCase);
    public string DriverHelpText => SelectedDriver is null
        ? "请选择已注册驱动。"
        : SelectedDriver.IsImplemented
            ? $"驱动键：{SelectedDriver.DriverKey}；当前可用于应用。"
            : $"驱动键：{SelectedDriver.DriverKey}；当前只登记了描述，不能启用应用，也不会自动回退仿真。";
    public bool CanSave => !string.IsNullOrWhiteSpace(Code)
        && !string.IsNullOrWhiteSpace(Name)
        && SelectedChannel is not null
        && SelectedDriver is not null;

    public bool TryBuild(out DeviceConfig.DeviceEntry result)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code)) errors.Add("设备编码不能为空");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("设备名称不能为空");
        if (SelectedChannel is null) errors.Add("请选择所属通道");
        if (SelectedDriver is null) errors.Add("请选择设备驱动");
        if (IsEnabled && SelectedDriver is { IsImplemented: false })
            errors.Add("未实现驱动只能保存为停用草稿，不能启用应用");
        if (IsS7 && string.IsNullOrWhiteSpace(CpuProfile))
            errors.Add("西门子 S7 设备必须填写 CPU Profile");

        var poll = ReadPositiveInt(PollIntervalMsText, "轮询周期", errors);
        var stale = ReadPositiveInt(StaleAfterMsText, "陈旧判定时间", errors);
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
            Protocol = _legacyProtocol,
            Address = _legacyAddress,
            ChannelId = SelectedChannel?.Id ?? string.Empty,
            DriverKey = SelectedDriver?.DriverKey ?? string.Empty,
            Manufacturer = Manufacturer.Trim(),
            Model = Model.Trim(),
            CpuProfile = CpuProfile.Trim(),
            ModbusUnitId = unitId,
            PollIntervalMs = poll,
            StaleAfterMs = stale,
            Enabled = IsEnabled
        };

        ValidationMessage = string.Join("；", errors.Distinct(StringComparer.Ordinal));
        return errors.Count == 0;
    }

    internal void SetValidation(string message) => ValidationMessage = message;

    private DeviceDriverChoice? AddUnknownDriver(string driverKey)
    {
        if (string.IsNullOrWhiteSpace(driverKey)) return null;
        var choice = new DeviceDriverChoice(
            driverKey.Trim(),
            $"未知驱动：{driverKey.Trim()}",
            false,
            new HashSet<DevicePointDataType>());
        DriverOptions.Add(choice);
        return choice;
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
                    descriptor.SupportedDataTypes);
            })
            .ToList();

        if (choices.Count == 0)
        {
            choices.Add(new DeviceDriverChoice(
                DriverKeyCatalog.Simulation,
                "仿真",
                true,
                new HashSet<DevicePointDataType>
                {
                    DevicePointDataType.Boolean,
                    DevicePointDataType.Bool,
                    DevicePointDataType.Decimal,
                    DevicePointDataType.Int32,
                    DevicePointDataType.Int16,
                    DevicePointDataType.UInt16,
                    DevicePointDataType.UInt32,
                    DevicePointDataType.Float32,
                    DevicePointDataType.String
                }));
        }
        return choices;
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
    private readonly DeviceMode _deviceMode;
    private readonly int _globalPollIntervalMs;
    private readonly int _globalTimeoutMs;
    private readonly IReadOnlyList<IDeviceDriverDescriptor> _descriptors;
    private readonly List<ChannelEntry> _channelEntries;
    private readonly List<DeviceConfig.DeviceEntry> _deviceEntries;

    public DeviceConfigurationEditorViewModel(
        DeviceConfig current,
        IEnumerable<PointsConfig.PointEntry> points,
        SignalBindingsConfig? bindings,
        IEnumerable<IDeviceDriverDescriptor>? descriptors,
        DeviceConfigurationService configurationService,
        UserContext actor)
    {
        ArgumentNullException.ThrowIfNull(current);
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _points = (points ?? Array.Empty<PointsConfig.PointEntry>()).Where(point => point is not null).ToList();
        _bindings = bindings ?? new SignalBindingsConfig();
        _deviceMode = current.DeviceMode;
        _globalPollIntervalMs = current.PollIntervalMs;
        _globalTimeoutMs = current.TimeoutMs;
        _descriptors = (descriptors ?? Array.Empty<IDeviceDriverDescriptor>()).ToList();
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
    public string DeviceModeText => _deviceMode == DeviceMode.Simulation ? "Simulation（仿真）" : "Hardware（硬件）";
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
    {
        if (!CanEdit) return;
        DeviceEditor = null;
        ChannelEditor = new ChannelEditorViewModel(
            null,
            TransportOptions,
            NextCode("CH", _channelEntries.Select(channel => channel.Code)));
        ValidationMessage = string.Empty;
    }

    public void EditSelectedChannel()
    {
        if (!CanEdit || SelectedChannel is null)
        {
            ValidationMessage = "请先选择一个通道";
            return;
        }
        DeviceEditor = null;
        ChannelEditor = new ChannelEditorViewModel(SelectedChannel.Entry, TransportOptions);
        ValidationMessage = string.Empty;
    }

    public void DeleteSelectedChannel()
    {
        if (!CanEdit || SelectedChannel is null)
        {
            ValidationMessage = "请先选择一个通道";
            return;
        }
        var selected = SelectedChannel.Entry;
        if (_deviceEntries.Any(device => string.Equals(device.ChannelId, selected.Id, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = $"通道“{selected.Code}”仍被设备引用，请先修改或删除这些设备；系统不会级联删除。";
            return;
        }
        _channelEntries.RemoveAll(channel => string.Equals(channel.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        RefreshRows();
        ValidationMessage = $"通道“{selected.Code}”已从候选中删除，保存并应用后才会生效。";
    }

    public bool CommitChannelEditor()
    {
        if (ChannelEditor is null) return true;
        if (!ChannelEditor.TryBuild(out var entry)) return false;
        if (_channelEntries.Any(channel => !string.Equals(channel.Id, entry.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(channel.Code, entry.Code, StringComparison.OrdinalIgnoreCase)))
        {
            ChannelEditor.SetValidation("通道编码已存在");
            return false;
        }
        var index = _channelEntries.FindIndex(channel =>
            string.Equals(channel.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) _channelEntries.Add(entry);
        else _channelEntries[index] = entry;
        ChannelEditor = null;
        RefreshRows();
        ValidationMessage = string.Empty;
        return true;
    }

    public void AddDevice()
    {
        if (!CanEdit) return;
        if (_channelEntries.Count == 0)
        {
            ValidationMessage = "请先新增至少一个通道，再新增设备";
            return;
        }
        ChannelEditor = null;
        DeviceEditor = new DeviceEditorViewModel(
            null,
            _channelEntries,
            _descriptors,
            NextCode("DEV", _deviceEntries.Select(device => device.Code)),
            SelectedChannel?.Id);
        ValidationMessage = string.Empty;
    }

    public void EditSelectedDevice()
    {
        if (!CanEdit || SelectedDevice is null)
        {
            ValidationMessage = "请先选择一个设备";
            return;
        }
        DeviceEditor = new DeviceEditorViewModel(SelectedDevice.Entry, _channelEntries, _descriptors);
        ChannelEditor = null;
        ValidationMessage = string.Empty;
    }

    public void DeleteSelectedDevice()
    {
        if (!CanEdit || SelectedDevice is null)
        {
            ValidationMessage = "请先选择一个设备";
            return;
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
            return;
        }
        _deviceEntries.RemoveAll(device => string.Equals(device.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        RefreshRows();
        ValidationMessage = $"设备“{selected.Code}”已从候选中删除，保存并应用后才会生效。";
    }

    public bool CommitDeviceEditor()
    {
        if (DeviceEditor is null) return true;
        if (!DeviceEditor.TryBuild(out var entry)) return false;
        if (_deviceEntries.Any(device => !string.Equals(device.Id, entry.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(device.Code, entry.Code, StringComparison.OrdinalIgnoreCase)))
        {
            DeviceEditor.SetValidation("设备编码已存在");
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

    public async Task<DeviceConfigurationApplyResult> SaveAsync(CancellationToken ct = default)
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
                    DeviceMode = _deviceMode,
                    PollIntervalMs = _globalPollIntervalMs,
                    TimeoutMs = _globalTimeoutMs,
                    Channels = _channelEntries.Select(CloneChannel).ToList(),
                    Devices = _deviceEntries.Select(CloneDevice).ToList()
                },
                ct);
            ValidationMessage = result.Ok
                ? $"设备配置已应用，生效版本：{result.Revision}"
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
        new(ChannelTransportKind.Simulation, "仿真", "离线仿真，不连接 TCP 或串口"),
        new(ChannelTransportKind.Tcp, "TCP", "连接网络设备，填写 Host 和 Port"),
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
            Tcp = source.Tcp is null ? null : new TcpChannelParameters { Host = source.Tcp.Host, Port = source.Tcp.Port },
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
            Manufacturer = source.Manufacturer,
            Model = source.Model,
            CpuProfile = source.CpuProfile,
            ModbusUnitId = source.ModbusUnitId,
            PollIntervalMs = source.PollIntervalMs,
            StaleAfterMs = source.StaleAfterMs,
            Enabled = source.Enabled
        };
}
