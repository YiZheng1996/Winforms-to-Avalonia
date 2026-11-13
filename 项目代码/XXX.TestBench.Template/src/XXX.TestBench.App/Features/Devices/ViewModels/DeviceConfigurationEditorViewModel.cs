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
/// 设备与通道配置编辑器。所有修改先保存在本地候选，保存时调用完整配置服务。
/// </summary>
public sealed partial class DeviceConfigurationEditorViewModel : ObservableObject
{
    private readonly DeviceConfigurationService _configurationService;
    private readonly UserContext _actor;
    private readonly List<PointsConfig.PointEntry> _points;
    private readonly SignalBindingsConfig _bindings;
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
                    Channels = _channelEntries.Select(CloneChannel).ToList(),
                    Devices = _deviceEntries.Select(CloneDevice).ToList()
                },
                new DeviceConfigurationApplyOptions(
                    ConfirmS7OptimizedBlockAccess: s7OptimizedBlockAccessConfirmed),
                ct);
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
            ChannelId = source.ChannelId,
            DriverKey = source.DriverKey,
            Model = source.Model,
            ModbusUnitId = source.ModbusUnitId,
            ModbusDataOrder = source.ModbusDataOrder is null ? null : new ModbusDataOrderOptions
            {
                ByteOrder = source.ModbusDataOrder.ByteOrder,
                WordOrder = source.ModbusDataOrder.WordOrder
            },
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
                DemotionPeriodMs = source.AutoDemotion.DemotionPeriodMs
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
