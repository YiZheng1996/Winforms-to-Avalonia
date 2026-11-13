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
    public const int CurrentTemplateVersion = 6;

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
        new("PointTag", "点位标签", true, "按“分组.点位”填写，例如 AI.L32；未分组时只填写点位名称。跨设备模板使用“设备编码/分组.点位”", "AI.L32"),
        new("Address", "地址", true, "直接填写所属设备的原始地址；Modbus 首选 C:0、DI:0、HR:0、IR:0，兼容明确功能区手册地址 00001/10001/30001/40001；S7-1200/1500 可使用 DB144.DBD88，S7-200 SMART 使用 VW5022", "HR:0"),
        new("DataType", "数据类型", true, "按设备手册选择并填写中文数据类型；系统会结合设备型号校验地址和类型", "浮点型"),
        new("ByteOrder", "字节序", false, "Modbus 寄存器点位可填写大端或小端；留空采用项目初始大端并在导入预览标记来源；非 Modbus 点位留空即可", "大端"),
        new("WordOrder", "字序", false, "Modbus Int16/UInt16 填 None；Int32/UInt32/Float32/Double 必须填写 HighWordFirst 或 LowWordFirst；非 Modbus 点位留空即可", "HighWordFirst"),
        new("Access", "访问权限", false, "只读或读写；启动、停止、复位、输出等读写点位仍按系统安全策略校验", "只读"),
        new("RawMin", "原始下限", false, "设备原始值下限；填写量程时四个量程单元格必须全部填写", "0"),
        new("RawMax", "原始上限", false, "设备原始值上限；填写量程时四个量程单元格必须全部填写", "10000"),
        new("EngMin", "工程下限", false, "换算后工程值下限；填写量程时四个量程单元格必须全部填写", "0"),
        new("EngMax", "工程上限", false, "换算后工程值上限；填写量程时四个量程单元格必须全部填写", "10"),
        new("Description", "说明", false, "填写点位用途、来源或现场备注，便于后续维护", "厂房进气压力")
        });

    private static readonly IReadOnlyList<TemplateChoice> RawDataTypeChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("字符", "单个字符，驱动按设备协议编码"),
        new TemplateChoice("字节", "一个字节的无符号整数，范围 0～255"),
        new TemplateChoice("短整型", "两个字节的有符号整数"),
        new TemplateChoice("字", "两个字节的无符号整数"),
        new TemplateChoice("长整型", "四个字节的有符号整数"),
        new TemplateChoice("双字", "四个字节的无符号整数"),
        new TemplateChoice("浮点型", "四个字节的 IEEE 754 单精度浮点数"),
        new TemplateChoice("双精度", "八个字节的 IEEE 754 双精度浮点数"),
        new TemplateChoice("布尔量", "Modbus Coil/DiscreteInput 的真/假值")
    ]);

    private static readonly IReadOnlyList<TemplateChoice> ByteOrderChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("大端", "高字节在前；Modbus 首版默认线路字节序"),
        new TemplateChoice("小端", "低字节在前；必须按设备手册确认")
    ]);

    private static readonly IReadOnlyList<TemplateChoice> WordOrderChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("无", "单寄存器或位类型使用；Int16/UInt16/Bool"),
        new TemplateChoice("高字在前", "多寄存器类型高字寄存器在前"),
        new TemplateChoice("低字在前", "多寄存器类型低字寄存器在前")
    ]);

    private static readonly IReadOnlyList<TemplateChoice> AccessChoicesDefinition = Array.AsReadOnly(
    [
        new TemplateChoice("只读", "只采集和显示数据，不允许人工写入"),
        new TemplateChoice("读写", "在受控流程中允许写入；高风险动作仍需更高权限和二次确认")
    ]);

    /// <summary>
    /// KEPServer 点位表字段到本项目中文模板的明确映射。
    /// 该映射只用于指导人工整理参考文件，不代表英文表头可以直接导入。
    /// </summary>
    private static readonly IReadOnlyList<ReferenceColumnMapping> ReferenceColumnMappingsDefinition =
        Array.AsReadOnly(
        new ReferenceColumnMapping[]
        {
            new("Tag Name", "点位标签", "项目直接使用“分组.点位”标签；设备范围只有多个设备时才在前面增加“设备编码/”，不再要求客户填写点位编码、设备编码和分组编码列。"),
            new("Address", "地址", "项目模板直接保留设备地址原文；导入时根据所属设备的驱动和型号校验，例如 S7-1200/1500 的 DB144.DBD88。"),
            new("Data Type", "数据类型", "按设备手册选择中文数据类型；Word 等泛型名称不能在未知有符号性的情况下直接套用。"),
            new("Byte Order / Word Order", "字节序 / 字序", "Modbus 寄存器按设备手册明确填写；字节序空白采用大端初始值，多寄存器字序不能猜测。"),
            new("Respect Data Type", "无直接对应列", "项目运行时始终按模板选定的数据类型解析，不另设一个可绕过配置的“遵循数据类型”开关。"),
            new("Client Access", "访问权限", "R 对应“只读”；R/W 对应“读写”。启动、停止、复位和输出点位仍由运行时安全策略识别和控制。"),
            new("Scan Rate", "设备配置中的采集周期", "采集周期属于设备，不在每个点位重复填写；点位清单显示设备继承的周期，需在设备编辑器中调整。"),
            new("Scaling / Raw Low / Raw High / Scaled Low / Scaled High", "原始/工程四项量程", "四项全部填写时启用线性换算；任一缺失都按未配置处理，项目不增加单独的缩放模式列。"),
            new("Scaled Data Type", "无直接对应列", "项目当前没有独立的工程数据类型；请以“原始数据类型”作为运行时解析类型。"),
            new("Clamp Low / Clamp High", "暂不支持", "当前配置模型和运行时没有限幅语义，不导入、不导出，避免产生看似生效但实际无效的配置。"),
            new("Eng Units", "不配置工程单位", "当前点位模板不包含工程单位字段；需要单位语义时由业务信号或驱动配置明确提供。"),
            new("Description", "说明", "直接填写项目模板中的“说明”，用于点位用途和现场备注。"),
            new("Negate Value", "暂不支持", "当前配置模型和运行时没有取反语义，不导入、不导出；需要取反时应在驱动或业务逻辑中明确实现并验证。")
        });

    /// <summary>
    /// 当前简化中文模板列定义，顺序即为 Excel/CSV 的固定列顺序。
    /// </summary>
    public static IReadOnlyList<ColumnDefinition> Columns => TemplateColumns;

    /// <summary>
    /// 当前模板的原始数据类型选项。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> RawDataTypeChoices => RawDataTypeChoicesDefinition;

    /// <summary>
    /// Modbus 寄存器字节序的模板下拉值。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> ByteOrderChoices => ByteOrderChoicesDefinition;

    /// <summary>
    /// Modbus 多寄存器字序的模板下拉值。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> WordOrderChoices => WordOrderChoicesDefinition;

    /// <summary>
    /// 当前模板中访问权限的客户可填写值。
    /// </summary>
    public static IReadOnlyList<TemplateChoice> AccessChoices => AccessChoicesDefinition;

    /// <summary>
    /// 参考 KEPServer 字段与本项目中文模板字段的映射说明。
    /// </summary>
    public static IReadOnlyList<ReferenceColumnMapping> ReferenceColumnMappings
        => ReferenceColumnMappingsDefinition;

    /// <summary>
    /// 把模板中的数据类型转换为运行时配置值。
    /// </summary>
    public static bool TryMapDataType(string value, out DevicePointDataType dataType)
        => DevicePointTypeCatalog.TryParseDataType(value, out dataType);

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

    /// <summary>
    /// 外部参考字段到项目字段的中文整理说明。
    /// </summary>
    public sealed record ReferenceColumnMapping(
        string ReferenceHeader,
        string ProjectField,
        string Handling);
}
