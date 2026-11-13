using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// 首版 Modbus 类型矩阵和字节/字序约束的唯一来源。
/// </summary>
public static class ModbusTypeCapabilities
{
    private static readonly IReadOnlySet<DevicePointDataType> Supported = new HashSet<DevicePointDataType>
    {
        DevicePointDataType.Bool,
        DevicePointDataType.Int16,
        DevicePointDataType.UInt16,
        DevicePointDataType.Int32,
        DevicePointDataType.UInt32,
        DevicePointDataType.Float32,
        DevicePointDataType.Double
    };

    public static IReadOnlySet<DevicePointDataType> SupportedTypes => Supported;

    public static bool IsSupported(DevicePointDataType type)
        => Supported.Contains(type) || type == DevicePointDataType.Boolean;

    public static bool IsBitType(DevicePointDataType type)
        => type is DevicePointDataType.Bool or DevicePointDataType.Boolean;

    public static bool TryGetRegisterCount(DevicePointDataType type, out int count)
    {
        count = type switch
        {
            DevicePointDataType.Int16 or DevicePointDataType.UInt16 => 1,
            DevicePointDataType.Int32 or DevicePointDataType.UInt32 or DevicePointDataType.Float32 => 2,
            DevicePointDataType.Double => 4,
            _ => 0
        };
        return count > 0;
    }

    /// <summary>
    /// 校验区域、数据类型、写权限和字节/字序是否构成一个可执行点位。
    /// </summary>
    public static bool TryValidate(
        ModbusArea area,
        DevicePointDataType type,
        bool isWritable,
        DecodeOptions? options,
        out string? reason)
    {
        if (!IsSupported(type))
        {
            reason = "Modbus 首版只支持 Bool、Int16、UInt16、Int32、UInt32、Float32 和 Double";
            return false;
        }

        var decode = options ?? new DecodeOptions { ByteOrder = ByteOrder.BigEndian, WordOrder = WordOrder.None };
        if (decode.ByteOrder is not (ByteOrder.BigEndian or ByteOrder.LittleEndian))
        {
            reason = "Modbus 字节序必须明确选择 BigEndian 或 LittleEndian";
            return false;
        }

        if (area.IsBitArea())
        {
            if (!IsBitType(type))
            {
                reason = "Coil/DiscreteInput 只能使用 Bool 类型";
                return false;
            }
            if (isWritable && area == ModbusArea.DiscreteInput)
            {
                reason = "DiscreteInput 是只读区，不能配置为可写";
                return false;
            }
            if (decode.WordOrder != WordOrder.None)
            {
                reason = "Coil/DiscreteInput 不使用字序，必须为 None";
                return false;
            }
            reason = null;
            return true;
        }

        if (IsBitType(type) || !TryGetRegisterCount(type, out var registerCount))
        {
            reason = "HoldingRegister/InputRegister 只能使用寄存器数值类型，不能使用 Bool";
            return false;
        }
        if (isWritable && area == ModbusArea.InputRegister)
        {
            reason = "InputRegister 是只读区，不能配置为可写";
            return false;
        }

        if (registerCount == 1 && decode.WordOrder != WordOrder.None)
        {
            reason = "单寄存器类型的字序必须为 None";
            return false;
        }
        if (registerCount > 1 && decode.WordOrder is not (WordOrder.HighWordFirst or WordOrder.LowWordFirst))
        {
            reason = "32/64 位 Modbus 类型的字序必须明确选择 HighWordFirst 或 LowWordFirst";
            return false;
        }
        reason = null;
        return true;
    }

    public static DevicePointDataType NormalizeCompatibilityType(
        ModbusArea area,
        DevicePointDataType type)
        => area.IsBitArea() && type == DevicePointDataType.Boolean
            ? DevicePointDataType.Bool
            : type;

    public static int GetRegisterCount(DevicePointDataType type)
        => TryGetRegisterCount(type, out var count)
            ? count
            : throw new DomainException($"Modbus 类型不占用寄存器：{type}");
}
