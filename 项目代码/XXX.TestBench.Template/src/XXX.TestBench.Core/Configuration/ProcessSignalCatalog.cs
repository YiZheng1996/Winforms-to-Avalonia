using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Execution;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 工艺气路页面使用的固定业务信号目录。
/// 目录只声明页面能力和校验规则，不创建点位、不创建执行器，也不提供任何默认硬件值。
/// </summary>
public sealed record ProcessSignalDefinition(
    string SignalKey,
    string DisplayName,
    SignalAccessKind Access,
    IReadOnlySet<DevicePointDataType> AcceptedDataTypes,
    string Unit,
    TimeSpan MaxSampleAge,
    bool RequiresEngineeringRange = false,
    decimal? ControlMin = null,
    decimal? ControlMax = null,
    decimal? Step = null)
{
    public bool IsWritable => Access == SignalAccessKind.Write;

    public bool IsNumeric => AcceptedDataTypes.Any(type => type is not (
        DevicePointDataType.Boolean or DevicePointDataType.Bool or DevicePointDataType.String
        or DevicePointDataType.Char));

    public string RequirementText
        => (IsWritable ? "写入" : "读取")
           + " · " + string.Join("/", AcceptedDataTypes.Select(type => DevicePointTypeCatalog.ToDisplayName(type)))
           + (string.IsNullOrWhiteSpace(Unit) ? string.Empty : " · " + Unit)
           + " · 样本不超过 " + MaxSampleAge.TotalSeconds.ToString("0.###") + " 秒";
}

/// <summary>
/// 工艺信号白名单。缺少某个绑定只影响对应控件，不会把所有工艺信号变成必需配置。
/// </summary>
public static class ProcessSignalCatalog
{
    public const string SafetyDoor = "Process.SafetyDoor";
    public const string ClampReady = "Process.ClampReady";
    public const string SupplyPressure = "Process.SupplyPressure";
    public const string MainPressure = "Process.MainPressure";
    public const string DutPressure = "Process.DutPressure";
    public const string InletCommand = "Process.InletCommand";
    public const string InletFeedback = "Process.InletFeedback";
    public const string ExhaustCommand = "Process.ExhaustCommand";
    public const string ExhaustFeedback = "Process.ExhaustFeedback";
    public const string PressureSetpoint = "Process.PressureSetpoint";
    public const string PressureSetpointReadback = "Process.PressureSetpointReadback";

    public static readonly IReadOnlySet<DevicePointDataType> BooleanTypes =
        new HashSet<DevicePointDataType>
        {
            DevicePointDataType.Boolean,
            DevicePointDataType.Bool
        };

    public static readonly IReadOnlySet<DevicePointDataType> NumericTypes =
        new HashSet<DevicePointDataType>
        {
            DevicePointDataType.Decimal,
            DevicePointDataType.Int16,
            DevicePointDataType.UInt16,
            DevicePointDataType.Int32,
            DevicePointDataType.UInt32,
            DevicePointDataType.Byte,
            DevicePointDataType.Float32,
            DevicePointDataType.Double
        };

    private static ProcessSignalDefinition ReadBool(string key, string name)
        => new(key, name, SignalAccessKind.Read, BooleanTypes, string.Empty, TimeSpan.FromSeconds(2));

    private static ProcessSignalDefinition ReadPressure(string key, string name)
        => new(key, name, SignalAccessKind.Read, NumericTypes, "MPa", TimeSpan.FromSeconds(2));

    private static ProcessSignalDefinition WriteBool(string key, string name)
        => new(key, name, SignalAccessKind.Write, BooleanTypes, string.Empty, TimeSpan.FromSeconds(2));

    public static IReadOnlyList<ProcessSignalDefinition> All { get; } = new[]
    {
        ReadBool(SafetyDoor, "安全门"),
        ReadBool(ClampReady, "夹紧到位"),
        ReadPressure(SupplyPressure, "气源压力"),
        ReadPressure(MainPressure, "主管压力"),
        ReadPressure(DutPressure, "被试件压力"),
        WriteBool(InletCommand, "进气阀"),
        ReadBool(InletFeedback, "进气阀反馈"),
        WriteBool(ExhaustCommand, "排气阀"),
        ReadBool(ExhaustFeedback, "排气阀反馈"),
        new ProcessSignalDefinition(
            PressureSetpoint,
            "调压设定",
            SignalAccessKind.Write,
            NumericTypes,
            "MPa",
            TimeSpan.FromSeconds(2),
            RequiresEngineeringRange: true,
            ControlMin: 0m,
            ControlMax: 1m,
            Step: 0.001m),
        ReadPressure(PressureSetpointReadback, "调压设定回读")
    };

    private static readonly IReadOnlyDictionary<string, ProcessSignalDefinition> ByKey =
        All.ToDictionary(item => item.SignalKey, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string? signalKey, out ProcessSignalDefinition definition)
        => ByKey.TryGetValue(signalKey?.Trim() ?? string.Empty, out definition!);

    public static ProcessSignalDefinition Get(string signalKey)
        => TryGet(signalKey, out var definition)
            ? definition
            : throw new KeyNotFoundException("工艺信号未注册：" + signalKey);

    public static bool IsProcessSignal(string? signalKey)
        => TryGet(signalKey, out _);

    public static bool IsNumericType(DevicePointDataType type)
        => NumericTypes.Contains(type);
}
