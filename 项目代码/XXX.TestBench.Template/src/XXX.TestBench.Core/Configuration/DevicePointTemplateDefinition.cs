namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 设备点位导入模板的固定格式定义。
/// 下载模板和导入校验必须共同使用这份定义，避免两边列名不一致。
/// </summary>
public static class DevicePointTemplateDefinition
{
    /// <summary>
    /// 模板第一个工作表名称。导入时只读取并校验这个工作表。
    /// </summary>
    public const string DataSheetName = "点位模板";

    /// <summary>
    /// 模板说明工作表名称。
    /// </summary>
    public const string InstructionsSheetName = "填写说明";

    /// <summary>
    /// 下载模板的默认文件名。
    /// </summary>
    public const string DefaultFileName = "设备点位导入模板.xlsx";

    private static readonly IReadOnlyList<ColumnDefinition> TemplateColumns = Array.AsReadOnly(
        new ColumnDefinition[]
    {
        new("Code", "点位编码", true, "唯一标识，不能重复", "AI_Pressure"),
        new("Name", "点位名称", false, "显示名称；留空时使用点位编码", "试验压力"),
        new("Protocol", "协议", true, "通信协议，例如 Simulation、S7、Modbus", "Simulation"),
        new("Address", "地址", true, "协议对应的点位地址", "sim.pressure"),
        new("DataType", "数据类型", true, "点位数据类型，例如 Decimal、Boolean、Int32", "Decimal"),
        new("Unit", "单位", false, "工程单位，没有单位时留空", "MPa"),
        new("RawMin", "原始下限", false, "原始量程下限；四个量程字段要么全部填写，要么全部留空", "0"),
        new("RawMax", "原始上限", false, "原始量程上限；四个量程字段要么全部填写，要么全部留空", "10000"),
        new("EngMin", "工程下限", false, "工程量程下限；四个量程字段要么全部填写，要么全部留空", "0"),
        new("EngMax", "工程上限", false, "工程量程上限；四个量程字段要么全部填写，要么全部留空", "10"),
        new("IsWritable", "可写", false, "只允许填写 是 或 否；留空按 否 处理", "否"),
        new("RiskLevel", "风险等级", false, "只允许填写 普通 或 高风险；留空按 普通 处理", "普通"),
        new("IsEnabled", "启用", false, "只允许填写 是 或 否；留空按 是 处理", "是"),
        new("Description", "说明", false, "点位用途或维护说明", "仿真压力输入")
        });

    /// <summary>
    /// 模板列定义，顺序即为 Excel/CSV 的固定列顺序。
    /// </summary>
    public static IReadOnlyList<ColumnDefinition> Columns => TemplateColumns;

    /// <summary>
    /// 模板列定义。
    /// </summary>
    public sealed record ColumnDefinition(
        string Field,
        string Header,
        bool IsRequired,
        string Description,
        string Example);
}
