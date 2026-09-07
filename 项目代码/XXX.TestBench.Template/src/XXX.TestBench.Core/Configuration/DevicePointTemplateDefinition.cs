using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 设备点位导入模板的固定格式定义。
/// 下载模板和导入校验必须共同使用这份定义，避免两边列名不一致。
/// </summary>
public static class DevicePointTemplateDefinition
{
    /// <summary>
    /// 当前导入模板版本。版本号只描述 Excel/CSV 边界，不等同于 points.json schemaVersion。
    /// </summary>
    public const int CurrentTemplateVersion = 3;

    /// <summary>
    /// 模板第一个工作表名称。导入时只读取并校验这个工作表。
    /// </summary>
    public const string DataSheetName = "点位模板";

    /// <summary>
    /// 模板说明工作表名称。
    /// </summary>
    public const string InstructionsSheetName = "填写说明";

    /// <summary>
    /// 模板示例工作表名称。该工作表只用于参考，导入器不会读取它。
    /// </summary>
    public const string ExamplesSheetName = "填写示例";

    /// <summary>
    /// 模板预留的填写行范围。
    /// </summary>
    public const int FirstDataRow = 2;

    /// <summary>
    /// 模板预留的最后一行。样式和下拉选项会覆盖到这一行。
    /// </summary>
    public const int LastDataRow = 1001;

    /// <summary>
    /// 下载模板的默认文件名。
    /// </summary>
    public const string DefaultFileName = "设备点位导入模板.xlsx";

    private static readonly IReadOnlyList<ColumnDefinition> TemplateColumns = Array.AsReadOnly(
        new ColumnDefinition[]
    {
        new("PointId", "点位标识", false, "稳定身份；新增点位可留空，更新已有点位时优先填写原标识", "留空或 9d2c2a1e-5d87-4d3a-b4fd-2a3f7fb4e901"),
        new("Code", "点位编码", true, "项目内唯一编码；更新时必须与点位标识对应的原编码一致", "AI_Pressure"),
        new("Name", "点位名称", false, "给操作员看的名称；留空时自动使用点位编码", "试验压力"),
        new("DeviceCode", "设备编码", true, "必须填写已存在的设备编码；不会通过点表隐式创建设备", "PLC_SIM_1"),
        new("GroupCode", "点位分组编码", false, "填写已存在的设备内分组编码；留空时归入该设备的 DEFAULT/未分组，不会隐式创建分组", "PRESSURE"),
        new("AddressType", "地址类型", true, "按所属设备驱动选择；仿真使用“仿真逻辑地址”，Modbus/S7 使用对应区域", "仿真逻辑地址"),
        new("AddressParameters", "地址参数", true, "填写可重新解析的规范地址参数；仿真示例为 sim.pressure", "sim.pressure"),
        new("RawDataType", "原始数据类型", true, "选择驱动先解码的原始类型；旧仿真小数可填写“小数”保留兼容语义", "单精度浮点数"),
        new("ByteOrder", "字节序", false, "多字节数据的字节顺序；未特殊指定时按设备驱动默认值填写", "大端"),
        new("WordOrder", "字序", false, "32 位数据的寄存器顺序；不适用时填写“无”", "无"),
        new("Unit", "单位", false, "工程值单位；没有单位时留空", "MPa"),
        new("RawMin", "原始下限", false, "设备原始值下限；填写量程时四个量程单元格必须全部填写", "0"),
        new("RawMax", "原始上限", false, "设备原始值上限；填写量程时四个量程单元格必须全部填写", "10000"),
        new("EngMin", "工程下限", false, "换算后工程值下限；填写量程时四个量程单元格必须全部填写", "0"),
        new("EngMax", "工程上限", false, "换算后工程值上限；填写量程时四个量程单元格必须全部填写", "10"),
        new("IsWritable", "可写", false, "填写“否”表示只读；只有确实需要受控写入时才填写“是”", "否"),
        new("IsEnabled", "启用", false, "填写“是”表示参与运行；暂不使用的点位可填写“否”", "是"),
        new("RiskLevel", "风险等级", false, "填写“普通”或“高风险”；启动、停止、复位、输出等动作请选择“高风险”", "普通"),
        new("Description", "说明", false, "填写点位用途、来源或现场备注，便于后续维护", "仿真压力输入")
        });

    // 仅作为旧单设备模板的显式兼容入口；下载模板和新数据都使用上面的 19 列定义。
    private static readonly IReadOnlyList<ColumnDefinition> LegacyTemplateColumns = Array.AsReadOnly(
        new ColumnDefinition[]
    {
        new("Code", "点位编码", true, "唯一标识，不能重复；建议与现场点表中的变量名保持一致", "AI_Pressure"),
        new("Name", "点位名称", false, "给操作员看的名称；留空时自动使用点位编码", "试验压力"),
        new("Protocol", "通信方式", true, "从下拉框选择系统如何解释设备地址；当前模板提供“仿真”", "仿真"),
        new("Address", "设备地址", true, "填写该通信方式对应的地址；仿真地址示例为 sim.pressure", "sim.pressure"),
        new("DataType", "数据类型", true, "从下拉框选择：开关量、小数、整数或文本", "小数"),
        new("Unit", "工程单位", false, "操作员看到的单位；没有单位时留空", "MPa"),
        new("RawMin", "原始下限", false, "设备原始值的下限；填写量程时四个量程单元格必须全部填写", "0"),
        new("RawMax", "原始上限", false, "设备原始值的上限；填写量程时四个量程单元格必须全部填写", "10000"),
        new("EngMin", "工程下限", false, "换算后工程值的下限；填写量程时四个量程单元格必须全部填写", "0"),
        new("EngMax", "工程上限", false, "换算后工程值的下限；填写量程时四个量程单元格必须全部填写", "10"),
        new("IsWritable", "是否允许写入", false, "填写“否”表示只读；只有确实需要下发命令或设定值时才填写“是”", "否"),
        new("RiskLevel", "写入风险", false, "填写“普通”或“高风险”；启动、停止、复位、输出等动作请选择“高风险”", "普通"),
        new("IsEnabled", "是否启用", false, "填写“是”表示参与运行；暂不使用的点位可填写“否”", "是"),
        new("Description", "说明", false, "填写点位用途、来源或现场备注，便于后续维护", "仿真压力输入")
    });

    private static readonly IReadOnlyList<TemplateChoice> ProtocolChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("仿真", "用于开发、演示和离线联调，不连接现场设备")
    ]);

    private static readonly IReadOnlyList<TemplateChoice> DataTypeChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("开关量", "只有是/否或开/关两种状态"),
        new TemplateChoice("小数", "压力、温度、电流等连续数值"),
        new TemplateChoice("整数", "不带小数的计数值或状态码"),
        new TemplateChoice("文本", "设备返回或需要写入的一段文字")
    ]);

    private static readonly IReadOnlyList<TemplateChoice> AddressTypeChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("仿真逻辑地址", "仿真运行时使用的逻辑地址，不解析为真实协议偏移"),
        new TemplateChoice("线圈", "Modbus Coil，地址参数填写统一零基偏移，可选位索引"),
        new TemplateChoice("离散输入", "Modbus Discrete Input，地址参数填写统一零基偏移"),
        new TemplateChoice("输入寄存器", "Modbus Input Register，地址参数填写统一零基偏移"),
        new TemplateChoice("保持寄存器", "Modbus Holding Register，地址参数填写统一零基偏移"),
        new TemplateChoice("S7 位", "S7 位地址，参数格式按已验证驱动 Profile 填写"),
        new TemplateChoice("S7 字节", "S7 字节地址，参数格式按已验证驱动 Profile 填写"),
        new TemplateChoice("S7 字", "S7 字地址，参数格式按已验证驱动 Profile 填写"),
        new TemplateChoice("S7 双字", "S7 双字地址，参数格式按已验证驱动 Profile 填写")
    ]);

    private static readonly IReadOnlyList<TemplateChoice> RawDataTypeChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("布尔量", "Bool，按位解码的原始值"),
        new TemplateChoice("16 位有符号整数", "Int16，两个字节的有符号整数"),
        new TemplateChoice("16 位无符号整数", "UInt16，两个字节的无符号整数"),
        new TemplateChoice("32 位有符号整数", "Int32，四个字节的有符号整数"),
        new TemplateChoice("32 位无符号整数", "UInt32，四个字节的无符号整数"),
        new TemplateChoice("单精度浮点数", "Float32，四个字节的 IEEE 754 单精度浮点数"),
        new TemplateChoice("小数", "Decimal，仅用于保留现有仿真点语义；不能据此推断现场寄存器类型")
    ]);

    private static readonly IReadOnlyList<TemplateChoice> ByteOrderChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("大端", "高位字节在前"),
        new TemplateChoice("小端", "低位字节在前")
    ]);

    private static readonly IReadOnlyList<TemplateChoice> WordOrderChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("无", "不适用或只有一个寄存器"),
        new TemplateChoice("高字在前", "高位寄存器在前"),
        new TemplateChoice("低字在前", "低位寄存器在前")
    ]);

    private static readonly IReadOnlyList<string> YesNoChoicesDefinition = Array.AsReadOnly(
        new[] { "是", "否" });

    /// <summary>
    /// 模板列定义，顺序即为 Excel/CSV 的固定列顺序。
    /// </summary>
    public static IReadOnlyList<ColumnDefinition> Columns => TemplateColumns;

    /// <summary>
    /// 旧版 14 列模板定义，仅供单设备兼容读取，不用于下载新模板。
    /// </summary>
    public static IReadOnlyList<ColumnDefinition> LegacyColumns => LegacyTemplateColumns;

    /// <summary>
    /// 模板中通信方式的客户可填写值及其说明。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> ProtocolChoices => ProtocolChoicesDefinition;

    /// <summary>
    /// 模板中数据类型的客户可填写值及其说明。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> DataTypeChoices => DataTypeChoicesDefinition;

    /// <summary>
    /// v2 地址类型选项。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> AddressTypeChoices => AddressTypeChoicesDefinition;

    /// <summary>
    /// v2 原始数据类型选项。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> RawDataTypeChoices => RawDataTypeChoicesDefinition;

    /// <summary>
    /// v2 字节序选项。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> ByteOrderChoices => ByteOrderChoicesDefinition;

    /// <summary>
    /// v2 字序选项。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> WordOrderChoices => WordOrderChoicesDefinition;

    /// <summary>
    /// 模板中布尔字段的客户可填写值。
    /// </summary>
    public static IReadOnlyList<string> YesNoChoices => YesNoChoicesDefinition;

    /// <summary>
    /// 把模板中的通信方式转换为运行时配置值。
    /// </summary>
    public static bool TryMapProtocol(string value, out DevicePointProtocol protocol)
        => DevicePointTypeCatalog.TryParseProtocol(value, out protocol);

    /// <summary>
    /// 把模板中的数据类型转换为运行时配置值。
    /// </summary>
    public static bool TryMapDataType(string value, out DevicePointDataType dataType)
        => DevicePointTypeCatalog.TryParseDataType(value, out dataType);

    /// <summary>
    /// 将模板地址类型转换为规范区域标识。驱动仍需对区域和范围做最终验证。
    /// </summary>
    public static bool TryMapAddressType(string? value, out string area)
    {
        switch ((value ?? string.Empty).Trim().Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant())
        {
            case "仿真逻辑地址":
            case "逻辑地址":
            case "simulation":
                area = "Simulation";
                return true;
            case "线圈":
            case "coil":
                area = "Coil";
                return true;
            case "离散输入":
            case "discreteinput":
                area = "DiscreteInput";
                return true;
            case "输入寄存器":
            case "inputregister":
                area = "InputRegister";
                return true;
            case "保持寄存器":
            case "holdingregister":
                area = "HoldingRegister";
                return true;
            case "s7位":
            case "s7bit":
                area = "S7.Bit";
                return true;
            case "s7字节":
            case "s7byte":
                area = "S7.Byte";
                return true;
            case "s7字":
            case "s7word":
                area = "S7.Word";
                return true;
            case "s7双字":
            case "s7dword":
                area = "S7.DWord";
                return true;
            default:
                area = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// 将模板字节序转换为领域枚举。
    /// </summary>
    public static bool TryMapByteOrder(string? value, out ByteOrder order)
    {
        switch ((value ?? string.Empty).Trim().Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant())
        {
            case "大端":
            case "bigendian":
                order = ByteOrder.BigEndian;
                return true;
            case "小端":
            case "littleendian":
                order = ByteOrder.LittleEndian;
                return true;
            default:
                order = ByteOrder.Unknown;
                return false;
        }
    }

    /// <summary>
    /// 将模板字序转换为领域枚举。
    /// </summary>
    public static bool TryMapWordOrder(string? value, out WordOrder order)
    {
        switch ((value ?? string.Empty).Trim().Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant())
        {
            case "无":
            case "none":
                order = WordOrder.None;
                return true;
            case "高字在前":
            case "highwordfirst":
                order = WordOrder.HighWordFirst;
                return true;
            case "低字在前":
            case "lowwordfirst":
                order = WordOrder.LowWordFirst;
                return true;
            default:
                order = WordOrder.None;
                return false;
        }
    }

    /// <summary>
    /// 模板列定义。
    /// </summary>
    public sealed record ColumnDefinition(
        string Field,
        string Header,
        bool IsRequired,
        string Description,
        string Example);

    /// <summary>
    /// 模板下拉选项，DisplayValue 是客户填写值。
    /// </summary>
    public sealed record TemplateChoice(
        string DisplayValue,
        string Description);
}
