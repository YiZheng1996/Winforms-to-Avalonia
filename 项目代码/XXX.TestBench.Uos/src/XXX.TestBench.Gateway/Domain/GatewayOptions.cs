using System.IO.Ports;
using System.Net;

namespace XXX.TestBench.Gateway.Domain;

public sealed class GatewayOptions
{
    public int SchemaVersion { get; set; } = 1;
    public string RunMode { get; set; } = "ReadOnly";
    public string TransportMode { get; set; } = "OfflineSimulation";
    public string ActiveModbusTransport { get; set; } = "Tcp";
    public bool DisableCalibrationWritesOnStartup { get; set; } = true;
    public int PollIntervalMs { get; set; } = 250;
    public int ConnectionTimeoutMs { get; set; } = 3_000;
    public List<S7EndpointOptions> S7Endpoints { get; set; } = [];
    public ModbusRtuOptions ModbusRtu { get; set; } = new();
    public ModbusTcpOptions ModbusTcp { get; set; } = new();

    public static GatewayOptions CreateDefault()
    {
        return new GatewayOptions
        {
            // The two profiles intentionally share the supplied address, but only one is
            // enabled. Two physical devices cannot be addressed by the same IP at once.
            S7Endpoints =
            [
                new S7EndpointOptions
                {
                    Id = "S7-200",
                    Model = "S7-200",
                    IpAddress = "192.168.0.111",
                    Rack = 0,
                    Slot = 2,
                    Enabled = true
                },
                new S7EndpointOptions
                {
                    Id = "S7-1200",
                    Model = "S7-1200",
                    IpAddress = "192.168.0.111",
                    Rack = 0,
                    Slot = 0,
                    Enabled = false
                }
            ],
            ModbusRtu = new ModbusRtuOptions
            {
                PortName = "COM1",
                BaudRate = 9_600,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                SlaveId = 1
            },
            ModbusTcp = new ModbusTcpOptions
            {
                Host = "127.0.0.1",
                Port = 502,
                UnitId = 1
            }
        };
    }

    public void Validate()
    {
        if (SchemaVersion != 1)
            throw new OptionsValidationException($"不支持的 Gateway 配置版本: {SchemaVersion}");

        if (!string.Equals(RunMode, "ReadOnly", StringComparison.OrdinalIgnoreCase))
            throw new OptionsValidationException("当前 Gateway 只允许 ReadOnly 模式，写入能力须经过 P3 安全门后单独启用。");

        if (!string.Equals(TransportMode, "OfflineSimulation", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(TransportMode, "ConfiguredDevices", StringComparison.OrdinalIgnoreCase))
            throw new OptionsValidationException("TransportMode 只能为 OfflineSimulation 或 ConfiguredDevices。");

        if (!string.Equals(ActiveModbusTransport, "Rtu", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ActiveModbusTransport, "Tcp", StringComparison.OrdinalIgnoreCase))
            throw new OptionsValidationException("ActiveModbusTransport 只能为 Rtu 或 Tcp。");

        if (!DisableCalibrationWritesOnStartup)
            throw new OptionsValidationException("DisableCalibrationWritesOnStartup 必须为 true。");

        if (PollIntervalMs is < 50 or > 60_000)
            throw new OptionsValidationException("PollIntervalMs 必须在 50 到 60000 毫秒之间。");

        if (ConnectionTimeoutMs is < 500 or > 120_000)
            throw new OptionsValidationException("ConnectionTimeoutMs 必须在 500 到 120000 毫秒之间。");

        var enabledS7 = S7Endpoints.Where(x => x.Enabled).ToList();
        if (enabledS7.Count != 1)
            throw new OptionsValidationException("当前只读切片必须且只能启用一个 S7 TCP 配置档案。");

        var duplicateS7 = enabledS7
            .GroupBy(x => $"{x.IpAddress}:{x.Port}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateS7 is not null)
            throw new OptionsValidationException($"启用的 S7 设备存在重复终点 {duplicateS7.Key}；不同物理设备必须使用不同 IP/端口。");

        foreach (var s7 in S7Endpoints)
            s7.Validate();

        ModbusRtu.Validate();
        ModbusTcp.Validate();

        if (string.Equals(TransportMode, "ConfiguredDevices", StringComparison.OrdinalIgnoreCase))
        {
            var activeModbusEnabled = string.Equals(ActiveModbusTransport, "Rtu", StringComparison.OrdinalIgnoreCase)
                ? ModbusRtu.Enabled
                : ModbusTcp.Enabled;
            if (!activeModbusEnabled)
                throw new OptionsValidationException($"ConfiguredDevices 模式要求活动 Modbus 链路 {ActiveModbusTransport} 已启用。");
        }
    }
}

public sealed class S7EndpointOptions
{
    public string Id { get; set; } = "S7-200";
    public string Model { get; set; } = "S7-200";
    public string IpAddress { get; set; } = "192.168.0.111";
    public int Port { get; set; } = 102;
    public short Rack { get; set; }
    public short Slot { get; set; } = 2;
    public int ReadTimeoutMs { get; set; } = 3_000;
    public bool Enabled { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Model))
            throw new OptionsValidationException("S7 配置必须包含 Id 和 Model。");
        if (!IPAddress.TryParse(IpAddress, out _))
            throw new OptionsValidationException($"S7 地址无效: {IpAddress}");
        if (Port is < 1 or > 65_535)
            throw new OptionsValidationException($"S7 端口无效: {Port}");
        if (Rack < 0 || Slot < 0)
            throw new OptionsValidationException("S7 Rack/Slot 不能为负数。");
        if (ReadTimeoutMs is < 500 or > 120_000)
            throw new OptionsValidationException("S7 ReadTimeoutMs 超出范围。");
    }
}

public sealed class ModbusRtuOptions
{
    public bool Enabled { get; set; } = true;
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9_600;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;
    public byte SlaveId { get; set; } = 1;
    public int ReadTimeoutMs { get; set; } = 3_000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName))
            throw new OptionsValidationException("Modbus RTU 串口不能为空。");
        if (BaudRate <= 0 || DataBits is < 5 or > 8)
            throw new OptionsValidationException("Modbus RTU 波特率或数据位无效。");
        if (StopBits == StopBits.None || SlaveId is 0 or > 247)
            throw new OptionsValidationException("Modbus RTU 停止位或站号无效。");
        if (ReadTimeoutMs is < 500 or > 120_000)
            throw new OptionsValidationException("Modbus RTU ReadTimeoutMs 超出范围。");
    }
}

public sealed class ModbusTcpOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public int ReadTimeoutMs { get; set; } = 3_000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new OptionsValidationException("Modbus TCP Host 不能为空。");
        if (Port is < 1 or > 65_535 || UnitId is 0 or > 247)
            throw new OptionsValidationException("Modbus TCP 端口或 UnitId 无效。");
        if (ReadTimeoutMs is < 500 or > 120_000)
            throw new OptionsValidationException("Modbus TCP ReadTimeoutMs 超出范围。");
    }
}

public sealed class OptionsValidationException(string message) : Exception(message);
