using System.Globalization;
using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// Modbus 地址解析器。
///
/// 首选格式是 C:0、DI:0、HR:0、IR:0，其中数字始终是协议零基偏移。
/// 为了兼容现场常见手册，明确带功能区前缀的 00001/10001/30001/40001
/// 也可以转换；裸数字 0/1 永远拒绝，防止把寄存器地址猜成错误的数据区。
/// </summary>
public static class ModbusAddressParser
{
    public static bool TryParse(string? text, out ModbusAddress address, out string? error)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            address = default;
            error = "Modbus 地址不能为空";
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator > 0)
        {
            var areaText = value[..separator].Trim();
            var offsetText = value[(separator + 1)..].Trim();
            if (!ModbusAreaExtensions.TryParse(areaText, out var area))
            {
                address = default;
                error = $"Modbus 数据区“{areaText}”不受支持，请使用 C、DI、HR 或 IR";
                return false;
            }

            if (!TryReadOffset(offsetText, out var offset, out error))
            {
                address = default;
                return false;
            }

            address = new ModbusAddress(area, offset);
            error = null;
            return true;
        }

        // 仅兼容“明确包含功能区前缀”的五位手册地址，例如 40001。
        // 裸 0、1、400 等没有功能区语义，不能靠猜测继续运行。
        if (value.Length == 5 && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var reference))
        {
            var prefix = value[0];
            var area = prefix switch
            {
                '0' => ModbusArea.Coil,
                '1' => ModbusArea.DiscreteInput,
                '3' => ModbusArea.InputRegister,
                '4' => ModbusArea.HoldingRegister,
                _ => (ModbusArea?)null
            };
            if (area.HasValue)
            {
                var offset = reference % 10000 - 1;
                if (offset >= 0 && offset <= ushort.MaxValue)
                {
                    address = new ModbusAddress(area.Value, offset);
                    error = null;
                    return true;
                }
            }
        }

        address = default;
        error = "Modbus 地址必须明确写成 C:偏移、DI:偏移、HR:偏移或 IR:偏移；兼容手册地址请使用 00001/10001/30001/40001 形式，不能填写裸数字";
        return false;
    }

    public static ModbusAddress Parse(string text)
        => TryParse(text, out var address, out var error)
            ? address
            : throw new DomainException(error ?? $"Modbus 地址无法解析：{text}");

    private static bool TryReadOffset(string text, out int offset, out string? error)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
        {
            error = "Modbus 地址偏移必须是整数";
            return false;
        }
        if (offset is < 0 or > ushort.MaxValue)
        {
            error = "Modbus 地址偏移必须在 0-65535 范围内";
            return false;
        }
        error = null;
        return true;
    }
}
