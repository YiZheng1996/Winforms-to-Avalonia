using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备校准点位表格中的一行，值与质量可被界面刷新。
/// </summary>
public sealed partial class PointRow : ObservableObject
{
    public required string Code { get; init; }
    public required string Address { get; init; }
    public string PointId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceText { get; init; } = "未分配设备";
    public string ModbusStationText { get; init; } = string.Empty;
    public bool HasModbusStation => !string.IsNullOrWhiteSpace(ModbusStationText);
    public required bool IsWritable { get; init; }
    public required WriteRiskLevel RiskLevel { get; init; }

    public static PointRow FromDevicePoint(
        DevicePoint point,
        IEnumerable<DeviceConfig.DeviceEntry>? devices)
    {
        var device = DeviceStationDisplay.ResolveDevice(devices, point.DeviceId);
        return new PointRow
        {
            Code = point.Code,
            Address = point.Address,
            PointId = point.PointId,
            DeviceId = point.DeviceId,
            DeviceText = DeviceStationDisplay.FormatDeviceText(device),
            ModbusStationText = DeviceStationDisplay.FormatLabel(device),
            IsWritable = point.IsWritable,
            RiskLevel = point.RiskLevel
        };
    }

    /// <summary>
    /// 点位当前的实时值文字。
    /// </summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>
    /// 点位当前的数据质量文字。
    /// </summary>
    [ObservableProperty]
    private string _quality = string.Empty;
}
