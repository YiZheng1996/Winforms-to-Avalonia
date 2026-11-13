namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointManagementViewModel
{
    /// <summary>
    /// 选中单台 Modbus 设备时显示站号；通道包含多台设备时不猜测某一个站号。
    /// </summary>
    public bool IsScopeDetailsModbusVisible
        => ResolveSelectedDevice() is { } device
            && DeviceStationDisplay.IsModbus(device.DriverKey);

    public string ScopeDetailsModbusUnitIdText
        => DeviceStationDisplay.FormatValue(ResolveSelectedDevice());
}
