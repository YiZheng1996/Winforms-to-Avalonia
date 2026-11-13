using System.Globalization;
using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Devices.Modbus;

public enum XinjeFieldV1Area
{
    D,
    HD,
    M,
    HM
}

/// <summary>
/// 信捷现场验证配置 V1 的原生地址及其 Modbus 零基地址。
/// </summary>
public readonly record struct XinjeFieldV1Address(
    XinjeFieldV1Area Area,
    int Number,
    ModbusAddress ModbusAddress)
{
    public string Canonical => $"{Area}{Number.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// 只解析当前现场基线中出现的 D、HD、M、HM 四类地址。
/// </summary>
public static class XinjeFieldV1AddressParser
{
    private const int HdRegisterBase = 41088;
    private const int HmCoilBase = 49408;

    public static bool TryParse(string? text, out XinjeFieldV1Address address, out string? error)
    {
        var value = (text ?? string.Empty).Trim();
        address = default;
        if (value.Length == 0)
        {
            error = "信捷地址不能为空；支持 D、HD、M、HM 四类地址，例如 D100、HD1000、M400、HM40";
            return false;
        }

        var (area, prefixLength) = GetArea(value);
        if (prefixLength == 0)
        {
            error = $"信捷地址“{value}”格式不受支持；仅支持 D、HD、M、HM 四类地址，不支持寄存器位地址";
            return false;
        }

        var numberText = value.AsSpan(prefixLength);
        if (numberText.IsEmpty || !ContainsOnlyAsciiDigits(numberText))
        {
            error = $"信捷地址“{value}”的数字部分必须是十进制非负整数，例如 D100、HD1000、M400、HM40";
            return false;
        }

        if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            error = $"信捷地址“{value}”的编号超出支持范围";
            return false;
        }

        var (modbusArea, baseOffset) = area switch
        {
            XinjeFieldV1Area.D => (ModbusArea.HoldingRegister, 0),
            XinjeFieldV1Area.HD => (ModbusArea.HoldingRegister, HdRegisterBase),
            XinjeFieldV1Area.M => (ModbusArea.Coil, 0),
            XinjeFieldV1Area.HM => (ModbusArea.Coil, HmCoilBase),
            _ => throw new ArgumentOutOfRangeException(nameof(area))
        };
        var offset = (long)baseOffset + number;
        if (offset is < 0 or > ushort.MaxValue)
        {
            error = $"信捷地址“{value}”换算后的 Modbus 偏移超出 0～65535";
            return false;
        }

        address = new XinjeFieldV1Address(area, number, new ModbusAddress(modbusArea, (int)offset));
        error = null;
        return true;
    }

    public static XinjeFieldV1Address Parse(string text)
        => TryParse(text, out var address, out var error)
            ? address
            : throw new DomainException(error ?? $"信捷地址无法解析：{text}");

    private static (XinjeFieldV1Area Area, int PrefixLength) GetArea(string value)
    {
        if (value.StartsWith("HD", StringComparison.OrdinalIgnoreCase))
            return (XinjeFieldV1Area.HD, 2);
        if (value.StartsWith("HM", StringComparison.OrdinalIgnoreCase))
            return (XinjeFieldV1Area.HM, 2);
        if (value.StartsWith('D') || value.StartsWith('d'))
            return (XinjeFieldV1Area.D, 1);
        if (value.StartsWith('M') || value.StartsWith('m'))
            return (XinjeFieldV1Area.M, 1);
        return (default, 0);
    }

    private static bool ContainsOnlyAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
                return false;
        }
        return true;
    }
}
