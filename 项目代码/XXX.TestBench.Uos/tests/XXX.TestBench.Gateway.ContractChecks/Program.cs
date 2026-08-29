using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;
using XXX.TestBench.Core.B11;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var options = GatewayOptions.CreateDefault();
options.Validate();
Assert(options.S7Endpoints.Count == 2, "默认 S7 配置档案数量错误。");
Assert(options.S7Endpoints.Single(x => x.Enabled).Model == "S7-200", "默认启用的 S7 档案错误。");
Assert(options.ModbusRtu.PortName == "COM1", "Modbus RTU 默认串口错误。");
Assert(options.ModbusRtu.BaudRate == 9600 && options.ModbusRtu.DataBits == 8, "Modbus RTU 默认串口参数错误。");
Assert(options.ModbusRtu.Parity == System.IO.Ports.Parity.None, "Modbus RTU 默认校验错误。");
Assert(options.ModbusTcp.Port == 502 && options.ModbusTcp.UnitId == 1, "Modbus TCP 默认参数错误。");

var session = new ReadOnlyGatewaySession();
Assert(!session.WritesEnabled, "只读 Gateway 暴露了写入能力。");
var unknown = session.Unknown<double>("SMART.PLC.AI.MAI00");
Assert(unknown.Quality == DataQuality.Unknown, "首样本不是 Unknown。");
session.MarkConnected();
var good = session.Accept("SMART.PLC.AI.MAI00", 12.5, DateTimeOffset.UtcNow);
Assert(good.Quality == DataQuality.Good && good.ConnectionGeneration == 1, "正常采样合同错误。");
session.MarkDisconnected();
var bad = session.Accept<double?>("SMART.PLC.AI.MAI00", 13.5, DateTimeOffset.UtcNow);
Assert(bad.Quality == DataQuality.Bad && bad.Value is null, "断线采样没有降级为 Bad。");

var settingsPath = args.FirstOrDefault();
if (!string.IsNullOrWhiteSpace(settingsPath))
{
    var document = GatewaySettingsFile.Load(settingsPath);
    Assert(document.FirstBusinessSlice == "PressureAdjustmentValveB11", "首条业务流程配置错误。");
    Assert(document.Gateway.DisableCalibrationWritesOnStartup, "启动校准写入保护未启用。");
}

// Constructing the adapters validates their configured endpoint without opening a device.
await using var s7 = new S7NetPlusReadOnlyTransport(options.S7Endpoints.Single(x => x.Enabled));
await using var modbusTcp = new NModbusReadOnlyTransport(options.ModbusTcp);
await using var modbusRtu = new NModbusReadOnlyTransport(options.ModbusRtu);
Assert(s7.ConnectionGeneration == 0 && !s7.IsConnected, "S7 适配器未保持初始断开状态。");
Assert(modbusTcp.ConnectionGeneration == 0 && !modbusTcp.IsConnected, "Modbus TCP 适配器未保持初始断开状态。");
Assert(modbusRtu.ConnectionGeneration == 0 && !modbusRtu.IsConnected, "Modbus RTU 适配器未保持初始断开状态。");

Assert(P2ReadOnlyPointCatalog.Points.Count == 5, "P2 五点只读切片数量错误。");
await using var simulatedS7 = new SimulatedS7ReadOnlyTransport();
var simulatedBeforeConnect = await simulatedS7.ReadMemoryAsync("VD200", 4);
Assert(simulatedBeforeConnect.Quality == DataQuality.Bad, "仿真 S7 未连接时没有返回 Bad。");
await simulatedS7.ConnectAsync();
var simulatedS7Good = await simulatedS7.ReadMemoryAsync("VD200", 4);
Assert(simulatedS7Good.Quality == DataQuality.Good && simulatedS7Good.Data.SequenceEqual(new byte[] { 0x42, 0x2A, 0x00, 0x00 }), "仿真 S7 只读样本错误。");
var firstGeneration = simulatedS7.ConnectionGeneration;
await simulatedS7.DisconnectAsync();
await simulatedS7.ConnectAsync();
Assert(simulatedS7.ConnectionGeneration > firstGeneration, "S7 重连没有递增连接代次。");

await using var simulatedModbus = new SimulatedModbusReadOnlyTransport();
await simulatedModbus.ConnectAsync();
var simulatedModbusGood = await simulatedModbus.ReadRegistersAsync(new ModbusRegisterReadRequest(1, 0, 2));
Assert(simulatedModbusGood.Quality == DataQuality.Good && simulatedModbusGood.Registers.SequenceEqual(new ushort[] { 1234, 5678 }), "仿真 Modbus 只读样本错误。");
await simulatedModbus.DisconnectAsync();
var simulatedModbusBad = await simulatedModbus.ReadRegistersAsync(new ModbusRegisterReadRequest(1, 0, 2));
Assert(simulatedModbusBad.Quality == DataQuality.Bad, "仿真 Modbus 断线没有返回 Bad。");

var b11Limits = new B11PressureAdjustmentLimits(40, 50, 20, 30);
var b11Retry = PressureAdjustmentWorkflow.Evaluate(
    b11Limits,
    new B11PressureAdjustmentObservation(55, 25, 1));
Assert(b11Retry.Decision == B11AdjustmentDecision.NeedsOperatorAdjustment, "B11 超范围时未要求人工调整。");
var b11Pass = PressureAdjustmentWorkflow.Evaluate(
    b11Limits,
    new B11PressureAdjustmentObservation(45, 25, 2));
Assert(b11Pass.Decision == B11AdjustmentDecision.Passed && b11Pass.ReportValues["val8"] == 45, "B11 合格判定或报表值错误。");
var b11Fail = PressureAdjustmentWorkflow.Evaluate(
    b11Limits,
    new B11PressureAdjustmentObservation(55, 25, 3));
Assert(b11Fail.Decision == B11AdjustmentDecision.Failed && b11Fail.IsTerminal, "B11 达到最大次数后未失败收口。");

Console.WriteLine("PASS gateway-contracts readonly=locked config=validated adapters=constructed-without-device-I/O");
