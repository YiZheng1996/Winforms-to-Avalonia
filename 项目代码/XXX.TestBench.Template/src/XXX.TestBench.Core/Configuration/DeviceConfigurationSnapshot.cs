namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 设备、点位、仿真和信号绑定的完整生效快照。
/// Revision 目录中的文件只能以完整快照形式生效，不能分别切换。
/// </summary>
public sealed class DeviceConfigurationSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Revision { get; set; } = string.Empty;
    public DeviceConfig Device { get; set; } = new();
    public PointsConfig Points { get; set; } = new();
    public SimulationConfig Simulation { get; set; } = new();
    public SignalBindingsConfig SignalBindings { get; set; } = new();
}
