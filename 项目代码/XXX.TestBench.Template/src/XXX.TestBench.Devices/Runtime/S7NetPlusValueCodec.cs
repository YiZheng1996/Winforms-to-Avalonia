using System.Buffers.Binary;
using System.Globalization;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// S7 原始字节与项目领域值之间的转换。
/// 默认按 S7 的 BigEndian/HighWordFirst 解释；显式 DecodeOptions 才执行逆序。
/// Decimal 是旧仿真的通用类型，真实 S7 没有对应 PLC 类型，硬件运行时拒绝猜测。
/// </summary>
public static class S7NetPlusValueCodec
{
    public static int GetByteCount(DevicePointDataType dataType)
        => dataType switch
        {
            DevicePointDataType.Boolean or DevicePointDataType.Bool => 1,
            DevicePointDataType.Byte or DevicePointDataType.Char => 1,
            DevicePointDataType.Int16 or DevicePointDataType.UInt16 => 2,
            DevicePointDataType.Int32 or DevicePointDataType.UInt32 or DevicePointDataType.Float32 => 4,
            DevicePointDataType.Double => 8,
            DevicePointDataType.Decimal => throw new DomainException(
                "西门子 S7 硬件不支持“小数”类型；请按 PLC 实际类型改为浮点型、长整型、双字、短整型或字"),
            DevicePointDataType.String => throw new DomainException("西门子 S7 硬件当前不支持字符串点位"),
            _ => throw new DomainException("西门子 S7 点位数据类型未知")
        };

    public static byte[] Encode(DevicePoint point, object? value)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (value is null)
            throw new DomainException($"点位“{DisplayPoint(point)}”的写入值不能为空");

        var type = EffectiveType(point);
        var count = GetByteCount(type);
        var canonical = type switch
        {
            DevicePointDataType.Boolean or DevicePointDataType.Bool => new[] { ToBoolean(value) ? (byte)1 : (byte)0 },
            DevicePointDataType.Byte => new[] { ToByte(value) },
            DevicePointDataType.Char => new[] { ToChar(value) },
            DevicePointDataType.Int16 => WriteInt16(ToInt16(value)),
            DevicePointDataType.UInt16 => WriteUInt16(ToUInt16(value)),
            DevicePointDataType.Int32 => WriteInt32(ToInt32(value)),
            DevicePointDataType.UInt32 => WriteUInt32(ToUInt32(value)),
            DevicePointDataType.Float32 => WriteSingle(ToSingle(value)),
            DevicePointDataType.Double => WriteDouble(ToDouble(value)),
            _ => throw new DomainException($"点位“{DisplayPoint(point)}”的数据类型“{DevicePointTypeCatalog.ToDisplayName(type)}”不支持西门子 S7 写入")
        };
        if (canonical.Length != count)
            throw new DomainException($"点位“{DisplayPoint(point)}”的编码字节数异常");
        return ApplyEncodeOptions(canonical, point.DecodeOptions);
    }

    public static object Decode(DevicePoint point, ReadOnlySpan<byte> source)
    {
        ArgumentNullException.ThrowIfNull(point);
        var type = EffectiveType(point);
        var count = GetByteCount(type);
        if (source.Length < count)
            throw new DomainException($"点位“{DisplayPoint(point)}”返回字节数不足：需要 {count}，实际 {source.Length}");
        var bytes = ApplyDecodeOptions(source[..count], point.DecodeOptions);
        return type switch
        {
            DevicePointDataType.Boolean or DevicePointDataType.Bool => bytes[0] != 0,
            DevicePointDataType.Byte => bytes[0],
            DevicePointDataType.Char => (char)bytes[0],
            DevicePointDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            DevicePointDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            DevicePointDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(bytes),
            DevicePointDataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            DevicePointDataType.Float32 => BinaryPrimitives.ReadSingleBigEndian(bytes),
            DevicePointDataType.Double => BinaryPrimitives.ReadDoubleBigEndian(bytes),
            _ => throw new DomainException($"点位“{DisplayPoint(point)}”的数据类型“{DevicePointTypeCatalog.ToDisplayName(type)}”不支持西门子 S7 读取")
        };
    }

    public static bool DecodeBit(DevicePoint point, byte raw, int bitIndex)
    {
        if (bitIndex is < 0 or > 7)
            throw new DomainException($"点位“{DisplayPoint(point)}”的位号必须在 0-7 范围内");
        return (raw & (1 << bitIndex)) != 0;
    }

    private static DevicePointDataType EffectiveType(DevicePoint point)
        => point.DataType;

    private static string DisplayPoint(DevicePoint point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;

    private static byte[] ApplyDecodeOptions(ReadOnlySpan<byte> source, DecodeOptions? options)
    {
        var bytes = source.ToArray();
        options ??= new DecodeOptions();
        if (options.WordOrder == WordOrder.LowWordFirst)
            SwapWords(bytes);
        if (options.ByteOrder == ByteOrder.LittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    private static byte[] ApplyEncodeOptions(byte[] source, DecodeOptions? options)
    {
        options ??= new DecodeOptions();
        if (options.ByteOrder == ByteOrder.LittleEndian)
            Array.Reverse(source);
        if (options.WordOrder == WordOrder.LowWordFirst)
            SwapWords(source);
        return source;
    }

    private static void SwapWords(byte[] bytes)
    {
        if (bytes.Length < 4) return;
        for (var offset = 0; offset + 3 < bytes.Length; offset += 4)
        {
            (bytes[offset], bytes[offset + 2]) = (bytes[offset + 2], bytes[offset]);
            (bytes[offset + 1], bytes[offset + 3]) = (bytes[offset + 3], bytes[offset + 1]);
        }
    }

    private static byte[] WriteInt16(short value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] WriteUInt16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] WriteInt32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] WriteUInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] WriteSingle(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        return bytes;
    }

    private static byte[] WriteDouble(double value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        return bytes;
    }

    private static bool ToBoolean(object value)
        => value is bool boolean
            ? boolean
            : bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
                ? parsed
                : throw new DomainException("S7 布尔点位需要布尔值");

    private static byte ToByte(object value)
        => value is byte b ? b : Convert.ToByte(value, CultureInfo.InvariantCulture);

    private static byte ToChar(object value)
    {
        if (value is char character) return checked((byte)character);
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return text is { Length: 1 } ? checked((byte)text[0]) : throw new DomainException("S7 字符点位需要单个字符");
    }

    private static short ToInt16(object value)
        => value is short v ? v : Convert.ToInt16(value, CultureInfo.InvariantCulture);

    private static ushort ToUInt16(object value)
        => value is ushort v ? v : Convert.ToUInt16(value, CultureInfo.InvariantCulture);

    private static int ToInt32(object value)
        => value is int v ? v : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static uint ToUInt32(object value)
        => value is uint v ? v : Convert.ToUInt32(value, CultureInfo.InvariantCulture);

    private static float ToSingle(object value)
        => value is float v ? v : Convert.ToSingle(value, CultureInfo.InvariantCulture);

    private static double ToDouble(object value)
        => value is double v ? v : Convert.ToDouble(value, CultureInfo.InvariantCulture);
}
