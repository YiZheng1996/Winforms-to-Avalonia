namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// 首版支持的 Modbus 数据区。
///
/// 区域码直接参与配置地址的规范化，避免仅凭一个数字地址猜测功能码。
/// </summary>
public enum ModbusArea
{
    Coil = 1,
    DiscreteInput = 2,
    HoldingRegister = 3,
    InputRegister = 4
}

public static class ModbusAreaExtensions
{
    public static bool IsBitArea(this ModbusArea area)
        => area is ModbusArea.Coil or ModbusArea.DiscreteInput;

    public static bool IsWritableArea(this ModbusArea area)
        => area is ModbusArea.Coil or ModbusArea.HoldingRegister;

    public static string ToCode(this ModbusArea area)
        => area switch
        {
            ModbusArea.Coil => "C",
            ModbusArea.DiscreteInput => "DI",
            ModbusArea.HoldingRegister => "HR",
            ModbusArea.InputRegister => "IR",
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "不支持的 Modbus 数据区")
        };

    public static bool TryParse(string? text, out ModbusArea area)
    {
        switch ((text ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "C":
            case "COIL":
            case "COILS":
                area = ModbusArea.Coil;
                return true;
            case "DI":
            case "DISCRETEINPUT":
            case "DISCRETEINPUTS":
            case "INPUTBIT":
                area = ModbusArea.DiscreteInput;
                return true;
            case "HR":
            case "HOLDINGREGISTER":
            case "HOLDINGREGISTERS":
                area = ModbusArea.HoldingRegister;
                return true;
            case "IR":
            case "INPUTREGISTER":
            case "INPUTREGISTERS":
                area = ModbusArea.InputRegister;
                return true;
            default:
                area = default;
                return false;
        }
    }
}

/// <summary>
/// 已经完成区域和零基偏移解析的 Modbus 地址。
/// </summary>
public readonly record struct ModbusAddress(ModbusArea Area, int Offset)
{
    public string Canonical => $"{Area.ToCode()}:{Offset}";
}
