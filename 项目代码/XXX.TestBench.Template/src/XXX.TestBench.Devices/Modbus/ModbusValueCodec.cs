using System.Buffers.Binary;
using System.Globalization;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// Modbus 寄存器值编解码器。
///
/// Modbus 在线路上按“大端字节的 16 位寄存器”传输。本类先把每个寄存器
/// 还原成字节，再按点位显式配置的 ByteOrder/WordOrder 调整，整个过程不依赖
/// 当前 Windows 机器的本机字节序，也不会把未配置的字序默认为某一种。
/// </summary>
public static class ModbusValueCodec
{
    public static object DecodeRegisters(DevicePoint point, ReadOnlySpan<ushort> registers)
    {
        ArgumentNullException.ThrowIfNull(point);
        var type = ResolveType(point);
        var count = ModbusTypeCapabilities.GetRegisterCount(type);
        if (registers.Length < count)
            throw new DomainException($"点位“{DisplayPoint(point)}”需要 {count} 个寄存器，实际只返回 {registers.Length} 个");

        var bytes = new byte[count * 2];
        for (var index = 0; index < count; index++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(index * 2, 2), registers[index]);
        ToLogicalBytes(bytes, point.DecodeOptions ?? throw new DomainException(
            $"点位“{DisplayPoint(point)}”未配置 Modbus 字节序和字序"));

        // 这些 arm 的数值类型不同。显式转换为 object，避免 C# 为 switch
        // 表达式推导出 double 的公共数值类型，导致 Int16/Int32 等协议值
        // 在装箱后错误地变成 Double。
        return type switch
        {
            DevicePointDataType.Int16 => (object)BinaryPrimitives.ReadInt16BigEndian(bytes),
            DevicePointDataType.UInt16 => (object)BinaryPrimitives.ReadUInt16BigEndian(bytes),
            DevicePointDataType.Int32 => (object)BinaryPrimitives.ReadInt32BigEndian(bytes),
            DevicePointDataType.UInt32 => (object)BinaryPrimitives.ReadUInt32BigEndian(bytes),
            DevicePointDataType.Float32 => (object)BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32BigEndian(bytes)),
            DevicePointDataType.Double => (object)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes)),
            _ => throw new DomainException($"点位“{DisplayPoint(point)}”不是 Modbus 寄存器数值类型")
        };
    }

    public static ushort[] EncodeRegisters(DevicePoint point, object value)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(value);
        var type = ResolveType(point);
        var count = ModbusTypeCapabilities.GetRegisterCount(type);
        var bytes = new byte[count * 2];
        switch (type)
        {
            case DevicePointDataType.Int16:
                BinaryPrimitives.WriteInt16BigEndian(bytes, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                break;
            case DevicePointDataType.UInt16:
                BinaryPrimitives.WriteUInt16BigEndian(bytes, Convert.ToUInt16(value, CultureInfo.InvariantCulture));
                break;
            case DevicePointDataType.Int32:
                BinaryPrimitives.WriteInt32BigEndian(bytes, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                break;
            case DevicePointDataType.UInt32:
                BinaryPrimitives.WriteUInt32BigEndian(bytes, Convert.ToUInt32(value, CultureInfo.InvariantCulture));
                break;
            case DevicePointDataType.Float32:
                BinaryPrimitives.WriteUInt32BigEndian(bytes,
                    BitConverter.SingleToUInt32Bits(Convert.ToSingle(value, CultureInfo.InvariantCulture)));
                break;
            case DevicePointDataType.Double:
                BinaryPrimitives.WriteInt64BigEndian(bytes,
                    BitConverter.DoubleToInt64Bits(Convert.ToDouble(value, CultureInfo.InvariantCulture)));
                break;
            default:
                throw new DomainException($"点位“{DisplayPoint(point)}”不是 Modbus 寄存器数值类型");
        }

        FromLogicalBytes(bytes, point.DecodeOptions ?? throw new DomainException(
            $"点位“{DisplayPoint(point)}”未配置 Modbus 字节序和字序"));
        var registers = new ushort[count];
        for (var index = 0; index < count; index++)
            registers[index] = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index * 2, 2));
        return registers;
    }

    private static DevicePointDataType ResolveType(DevicePoint point)
    {
        var address = ModbusAddressParser.Parse(point.Address);
        var type = ModbusTypeCapabilities.NormalizeCompatibilityType(address.Area, point.DataType);
        if (!ModbusTypeCapabilities.TryValidate(address.Area, type, point.IsWritable, point.DecodeOptions, out var reason))
            throw new DomainException($"点位“{DisplayPoint(point)}”配置无效：{reason}");
        if (ModbusTypeCapabilities.IsBitType(type))
            throw new DomainException($"点位“{DisplayPoint(point)}”是位类型，不能使用寄存器编解码");
        return type;
    }

    private static void ToLogicalBytes(byte[] bytes, DecodeOptions options)
    {
        if (options.WordOrder == WordOrder.LowWordFirst)
            SwapWords(bytes);
        if (options.ByteOrder == ByteOrder.LittleEndian)
            SwapBytesInsideWords(bytes);
    }

    private static void FromLogicalBytes(byte[] bytes, DecodeOptions options)
    {
        if (options.ByteOrder == ByteOrder.LittleEndian)
            SwapBytesInsideWords(bytes);
        if (options.WordOrder == WordOrder.LowWordFirst)
            SwapWords(bytes);
    }

    private static void SwapBytesInsideWords(byte[] bytes)
    {
        for (var index = 0; index + 1 < bytes.Length; index += 2)
            (bytes[index], bytes[index + 1]) = (bytes[index + 1], bytes[index]);
    }

    private static void SwapWords(byte[] bytes)
    {
        if (bytes.Length <= 2) return;
        var wordCount = bytes.Length / 2;
        var copy = (byte[])bytes.Clone();
        for (var target = 0; target < wordCount; target++)
        {
            var source = wordCount - target - 1;
            bytes[target * 2] = copy[source * 2];
            bytes[target * 2 + 1] = copy[source * 2 + 1];
        }
    }

    private static string DisplayPoint(DevicePoint point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;
}
