using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 统一处理原始值和工程值。读路径把 Value 暴露为工程值，RawValue 保留协议解码后的原始值；写路径反向使用同一公式。
/// </summary>
public static class DevicePointValueConverter
{
    public static object? ToEngineering(DevicePoint point, object? rawValue)
    {
        if (!HasScale(point)) return rawValue;
        var raw = ToDecimal(rawValue, point, "原始值");
        ValidateScale(point);
        var rawMin = point.RawMin!.Value;
        var rawMax = point.RawMax!.Value;
        var engMin = point.EngMin!.Value;
        var engMax = point.EngMax!.Value;
        var engineering = engMin + (raw - rawMin) * (engMax - engMin) / (rawMax - rawMin);
        return engineering;
    }

    public static object? ToRaw(DevicePoint point, object? engineeringValue)
    {
        if (!HasScale(point)) return ConvertToType(point.DataType, engineeringValue, point.Code);
        var engineering = ToDecimal(engineeringValue, point, "工程值");
        ValidateScale(point);
        var rawMin = point.RawMin!.Value;
        var rawMax = point.RawMax!.Value;
        var engMin = point.EngMin!.Value;
        var engMax = point.EngMax!.Value;
        if (engineering < Math.Min(engMin, engMax) || engineering > Math.Max(engMin, engMax))
            throw new DomainException($"点位 {point.Code} 的工程值超出量程");

        var raw = rawMin + (engineering - engMin) * (rawMax - rawMin) / (engMax - engMin);
        if (point.DataType is DevicePointDataType.Int16
            or DevicePointDataType.UInt16
            or DevicePointDataType.Int32
            or DevicePointDataType.UInt32
            or DevicePointDataType.Byte)
            raw = decimal.Round(raw, 0, MidpointRounding.AwayFromZero);
        if (raw < Math.Min(rawMin, rawMax) || raw > Math.Max(rawMin, rawMax))
            throw new DomainException($"点位 {point.Code} 的原始值超出量程");
        return ConvertToType(point.DataType, raw, point.Code);
    }

    public static bool RawValuesEqual(DevicePoint point, object? expected, object? actual)
    {
        if (expected is null || actual is null) return expected is null && actual is null;
        if (IsNumeric(point.DataType) && TryDecimal(expected, out var left) && TryDecimal(actual, out var right))
            return Math.Abs(left - right) <= 0.0001m;
        return Equals(expected, actual);
    }

    public static bool HasScale(DevicePoint point)
        => point.RawMin.HasValue || point.RawMax.HasValue || point.EngMin.HasValue || point.EngMax.HasValue;

    public static bool IsNumeric(DevicePointDataType dataType)
        => dataType is DevicePointDataType.Decimal
            or DevicePointDataType.Int16
            or DevicePointDataType.UInt16
            or DevicePointDataType.Int32
            or DevicePointDataType.UInt32
            or DevicePointDataType.Float32
            or DevicePointDataType.Double
            or DevicePointDataType.Byte;

    private static void ValidateScale(DevicePoint point)
    {
        if (!point.RawMin.HasValue || !point.RawMax.HasValue || !point.EngMin.HasValue || !point.EngMax.HasValue)
            throw new DomainException($"点位 {point.Code} 的量程必须完整填写原始/工程上下限");
        if (point.RawMin == point.RawMax || point.EngMin == point.EngMax)
            throw new DomainException($"点位 {point.Code} 的量程上下限不能相等");
    }

    private static decimal ToDecimal(object? value, DevicePoint point, string label)
    {
        if (TryDecimal(value, out var result)) return result;
        throw new DomainException($"点位 {point.Code} 的{label}不是有效数值");
    }

    private static bool TryDecimal(object? value, out decimal result)
    {
        try
        {
            if (value is null)
            {
                result = 0;
                return false;
            }
            result = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static object? ConvertToType(DevicePointDataType dataType, object? value, string code)
    {
        try
        {
            return dataType switch
            {
                DevicePointDataType.Boolean or DevicePointDataType.Bool => Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.Byte => Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.Char => value is char character ? character : Convert.ToChar(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.Int16 => Convert.ToInt16(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.UInt16 => Convert.ToUInt16(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.Int32 => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.UInt32 => Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.Float32 => Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.Double => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.Decimal => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
                DevicePointDataType.String => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                _ => throw new DomainException($"点位 {code} 的数据类型不支持值转换：{dataType}")
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new DomainException($"点位 {code} 的值无法转换为 {dataType}");
        }
    }
}
