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
/// Modbus 设备级字节序选项。显示名称使用现场配置语言，内部值只用于运行时编解码。
/// </summary>
public sealed record ModbusByteOrderChoice(ByteOrder Value, string DisplayName, string Description)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Modbus 设备级多寄存器字序选项。
/// </summary>
public sealed record ModbusWordOrderChoice(WordOrder Value, string DisplayName, string Description)
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
/// 串口参数的客户可读选项。Value 保留协议和配置使用的内部值，DisplayName 用于界面显示。
/// </summary>
public sealed record SerialSettingChoice(string Value, string DisplayName)
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
    public string ModbusStationText => DeviceStationDisplay.FormatLabel(Entry);
    public string DetailsText => $"{DriverText} · 通道：{ChannelText} · {PointCount} 个点位"
        + (string.IsNullOrWhiteSpace(ModbusStationText) ? string.Empty : $" · {ModbusStationText}");
    public string ModeText => Entry.DeviceMode == DeviceMode.Simulation ? "仿真模式" : "硬件模式";
}

