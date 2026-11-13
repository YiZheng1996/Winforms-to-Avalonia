using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Devices.Siemens;

/// <summary>
/// 当前 S7 运行时实际实现的数据类型白名单，避免 UI 可选类型与协议实现分叉。
/// </summary>
public static class SiemensS7TypeCapabilities
{
    public static IReadOnlySet<DevicePointDataType> SupportedTypes { get; } =
        new HashSet<DevicePointDataType>
        {
            DevicePointDataType.Boolean,
            DevicePointDataType.Bool,
            DevicePointDataType.Byte,
            DevicePointDataType.Char,
            DevicePointDataType.Int16,
            DevicePointDataType.UInt16,
            DevicePointDataType.Int32,
            DevicePointDataType.UInt32,
            DevicePointDataType.Float32
        };

    public static bool IsSupported(DevicePointDataType dataType, out string reason)
    {
        if (SupportedTypes.Contains(dataType))
        {
            reason = string.Empty;
            return true;
        }
        reason = dataType == DevicePointDataType.Decimal
            ? "硬件模式不支持“小数”类型；请按 PLC 实际类型改为浮点型、长整型、双字、短整型或字"
            : $"S7 当前不支持数据类型 {dataType}；支持 Boolean/Bool、Byte/Char、Int16/UInt16、Int32/UInt32、Float32";
        return false;
    }

    public static bool IsShapeCompatible(S7AddressShape shape, DevicePointDataType dataType, out string reason)
    {
        if (!IsSupported(dataType, out reason)) return false;
        var compatible = shape switch
        {
            S7AddressShape.Bit => dataType is DevicePointDataType.Boolean or DevicePointDataType.Bool,
            S7AddressShape.Byte => dataType is DevicePointDataType.Byte or DevicePointDataType.Char,
            S7AddressShape.Word => dataType is DevicePointDataType.Int16 or DevicePointDataType.UInt16,
            S7AddressShape.DoubleWord => dataType is DevicePointDataType.Int32 or DevicePointDataType.UInt32 or DevicePointDataType.Float32,
            _ => false
        };
        if (!compatible)
            reason = $"S7 地址形状 {shape} 与数据类型 {dataType} 不匹配";
        return compatible;
    }
}
