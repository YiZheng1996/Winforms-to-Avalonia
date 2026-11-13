namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 设备点位通信方式。Unknown 只用于识别旧配置或非法输入，不能通过配置校验。
/// </summary>
public enum DevicePointProtocol
{
    Unknown = 0,
    Simulation = 1,
    ModbusRtu = 2,
    ModbusTcp = 3,
    SiemensS7 = 4
}

/// <summary>
/// 设备点位数据类型。
/// </summary>
public enum DevicePointDataType
{
    Unknown = 0,
    Boolean = 1,
    Decimal = 2,
    Int32 = 3,
    String = 4,
    Bool = 5,
    Int16 = 6,
    UInt16 = 7,
    UInt32 = 8,
    Float32 = 9,
    Byte = 10,
    Double = 11,
    Char = 12
}

/// <summary>
/// 配置中使用的稳定驱动键。
/// </summary>
public static class DriverKeyCatalog
{
    public const string Simulation = "simulation";
    public const string ModbusRtu = "modbus-rtu";
    public const string ModbusTcp = "modbus-tcp";
    public const string SiemensS7 = "siemens-s7";
}

/// <summary>
/// 点位类型的统一解析和显示入口。
/// 字符串只允许出现在 JSON、Excel/CSV 等外部边界，核心业务使用枚举。
/// </summary>
public static class DevicePointTypeCatalog
{
    /// <summary>
    /// 根据设备驱动键推导兼容的点位协议类型；驱动键本身仍由设备配置持有。
    /// </summary>
    public static bool TryParseDriverKey(string? value, out DevicePointProtocol protocol)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case DriverKeyCatalog.Simulation:
                protocol = DevicePointProtocol.Simulation;
                return true;
            case DriverKeyCatalog.ModbusRtu:
                protocol = DevicePointProtocol.ModbusRtu;
                return true;
            case DriverKeyCatalog.ModbusTcp:
                protocol = DevicePointProtocol.ModbusTcp;
                return true;
            case DriverKeyCatalog.SiemensS7:
                protocol = DevicePointProtocol.SiemensS7;
                return true;
            default:
                protocol = DevicePointProtocol.Unknown;
                return false;
        }
    }

    /// <summary>
    /// 解析通信方式，兼容运行时英文值和模板中文值。
    /// </summary>
    public static bool TryParseProtocol(string? value, out DevicePointProtocol protocol)
    {
        switch (Normalize(value))
        {
            case "仿真":
            case "simulation":
            case "仿真(simulation)":
                protocol = DevicePointProtocol.Simulation;
                return true;
            case "modbusrtu":
            case "modbus-rtu":
            case "modbus rtu":
            case "modbusrtu(modbus-rtu)":
                protocol = DevicePointProtocol.ModbusRtu;
                return true;
            case "modbustcp":
            case "modbus-tcp":
            case "modbus tcp":
            case "modbustcp(modbus-tcp)":
                protocol = DevicePointProtocol.ModbusTcp;
                return true;
            case "siemenss7":
            case "siemens-s7":
            case "西门子s7":
                protocol = DevicePointProtocol.SiemensS7;
                return true;
            default:
                protocol = DevicePointProtocol.Unknown;
                return false;
        }
    }

    /// <summary>
    /// 解析数据类型，兼容运行时英文值和模板中文值。
    /// </summary>
    public static bool TryParseDataType(string? value, out DevicePointDataType dataType)
    {
        switch (Normalize(value))
        {
            case "开关量":
            case "boolean":
            case "开关量(boolean)":
                dataType = DevicePointDataType.Boolean;
                return true;
            case "布尔":
            case "bool":
            case "布尔(bool)":
            case "布尔量":
            case "布尔量(bool)":
                dataType = DevicePointDataType.Bool;
                return true;
            case "字符":
            case "char":
            case "字符(char)":
                dataType = DevicePointDataType.Char;
                return true;
            case "字节":
            case "byte":
            case "uint8":
            case "字节(byte)":
                dataType = DevicePointDataType.Byte;
                return true;
            case "小数":
            case "decimal":
            case "小数(decimal)":
                dataType = DevicePointDataType.Decimal;
                return true;
            case "整数":
            case "int32":
            case "整数(int32)":
            case "长整型":
            case "长整型(int32)":
            case "long":
            case "32位有符号整数":
            case "32位有符号整数(int32)":
                dataType = DevicePointDataType.Int32;
                return true;
            case "int16":
            case "短整型":
            case "短整型(int16)":
            case "16位有符号整数":
            case "16位有符号整数(int16)":
                dataType = DevicePointDataType.Int16;
                return true;
            case "uint16":
            case "字":
            case "字(uint16)":
            case "16位无符号整数":
            case "16位无符号整数(uint16)":
                dataType = DevicePointDataType.UInt16;
                return true;
            case "uint32":
            case "双字":
            case "双字(uint32)":
            case "32位无符号整数":
            case "32位无符号整数(uint32)":
                dataType = DevicePointDataType.UInt32;
                return true;
            case "float32":
            case "float":
            case "real":
            case "single":
            case "浮点型":
            case "浮点型(float32)":
            case "单精度":
            case "单精度(float32)":
            case "单精度浮点数":
            case "单精度浮点数(float32)":
                dataType = DevicePointDataType.Float32;
                return true;
            case "double":
            case "float64":
            case "双精度":
            case "双精度(double)":
                dataType = DevicePointDataType.Double;
                return true;
            case "int":
                dataType = DevicePointDataType.Int32;
                return true;
            case "short":
                dataType = DevicePointDataType.Int16;
                return true;
            case "word":
            case "ushort":
                dataType = DevicePointDataType.UInt16;
                return true;
            case "dword":
            case "uint":
                dataType = DevicePointDataType.UInt32;
                return true;
            case "字符串":
            case "字符串(string)":
            case "文本":
            case "string":
            case "文本(string)":
                dataType = DevicePointDataType.String;
                return true;
            default:
                dataType = DevicePointDataType.Unknown;
                return false;
        }
    }

    /// <summary>
    /// 返回配置文件使用的稳定英文名称。
    /// </summary>
    public static string ToStorage(DevicePointProtocol protocol)
        => protocol switch
        {
            DevicePointProtocol.Simulation => nameof(DevicePointProtocol.Simulation),
            DevicePointProtocol.ModbusRtu => nameof(DevicePointProtocol.ModbusRtu),
            DevicePointProtocol.ModbusTcp => nameof(DevicePointProtocol.ModbusTcp),
            DevicePointProtocol.SiemensS7 => nameof(DevicePointProtocol.SiemensS7),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "不支持的设备点位通信方式")
        };

    /// <summary>
    /// 返回配置文件使用的稳定英文名称。
    /// </summary>
    public static string ToStorage(DevicePointDataType dataType)
        => dataType switch
        {
            DevicePointDataType.Boolean => nameof(DevicePointDataType.Boolean),
            DevicePointDataType.Decimal => nameof(DevicePointDataType.Decimal),
            DevicePointDataType.Int32 => nameof(DevicePointDataType.Int32),
            DevicePointDataType.String => nameof(DevicePointDataType.String),
            DevicePointDataType.Bool => nameof(DevicePointDataType.Bool),
            DevicePointDataType.Int16 => nameof(DevicePointDataType.Int16),
            DevicePointDataType.UInt16 => nameof(DevicePointDataType.UInt16),
            DevicePointDataType.UInt32 => nameof(DevicePointDataType.UInt32),
            DevicePointDataType.Float32 => nameof(DevicePointDataType.Float32),
            DevicePointDataType.Byte => nameof(DevicePointDataType.Byte),
            DevicePointDataType.Double => nameof(DevicePointDataType.Double),
            DevicePointDataType.Char => nameof(DevicePointDataType.Char),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "不支持的设备点位数据类型")
        };

    /// <summary>
    /// 返回客户页面使用的中文通信方式名称。
    /// </summary>
    public static string ToDisplayName(DevicePointProtocol protocol, string? fallback = null)
        => protocol switch
        {
            DevicePointProtocol.Simulation => "仿真",
            DevicePointProtocol.ModbusRtu => "Modbus RTU",
            DevicePointProtocol.ModbusTcp => "Modbus TCP",
            DevicePointProtocol.SiemensS7 => "西门子 S7",
            _ => string.IsNullOrWhiteSpace(fallback) ? "未知" : fallback.Trim()
        };

    /// <summary>
    /// 返回客户页面使用的中文数据类型名称。
    /// </summary>
    public static string ToDisplayName(DevicePointDataType dataType, string? fallback = null)
        => dataType switch
        {
            DevicePointDataType.Boolean => "开关量",
            DevicePointDataType.Bool => "布尔量",
            DevicePointDataType.Decimal => "小数",
            DevicePointDataType.Int32 => "长整型",
            DevicePointDataType.Int16 => "短整型",
            DevicePointDataType.UInt16 => "字",
            DevicePointDataType.UInt32 => "双字",
            DevicePointDataType.Float32 => "浮点型",
            DevicePointDataType.Double => "双精度",
            DevicePointDataType.Byte => "字节",
            DevicePointDataType.Char => "字符",
            DevicePointDataType.String => "字符串",
            _ => string.IsNullOrWhiteSpace(fallback) ? "未知" : fallback.Trim()
        };

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim()
            .Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
