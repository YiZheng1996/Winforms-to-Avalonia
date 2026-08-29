namespace XXX.TestBench.Gateway.Domain;

public enum GatewayPointValueType
{
    Boolean,
    Float32,
    UInt16
}

public sealed record GatewayPointDefinition(
    string LogicalPoint,
    string Description,
    string? PhysicalAddress,
    GatewayPointValueType ValueType,
    string Transport,
    bool FirstSlice = true);

public static class P2ReadOnlyPointCatalog
{
    public static IReadOnlyList<GatewayPointDefinition> Points { get; } =
    [
        new("Gateway.Health.NoError", "Gateway 无错误", null, GatewayPointValueType.Boolean, "Derived"),
        new("Gateway.Health.Simulated", "Gateway 仿真标识", null, GatewayPointValueType.Boolean, "Derived"),
        new("SMART.PLC.AI.MAI00", "模拟量输入 00", "VD200", GatewayPointValueType.Float32, "S7"),
        new("SMART.PLC.DI.MDI00", "数字量输入 00", "V0.0", GatewayPointValueType.Boolean, "S7"),
        new("Modbus.WSD.CH00", "WSD 通道 00", "300001", GatewayPointValueType.UInt16, "Modbus")
    ];
}
