using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 设备通信配置信息。
/// </summary>
public sealed class DeviceConfig
{
    /// <summary>
    /// 配置结构版本号，必须与程序支持的版本一致。
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>
    /// 设备运行模式，模拟或硬件。
    /// </summary>
    public DeviceMode DeviceMode { get; set; }
    /// <summary>
    /// 数据轮询间隔，单位毫秒。
    /// </summary>
    public int PollIntervalMs { get; set; } = 500;
    /// <summary>
    /// 通信超时时间，单位毫秒。
    /// </summary>
    public int TimeoutMs { get; set; } = 1000;
    /// <summary>
    /// 设备连接项列表。
    /// </summary>
    public List<DeviceEntry> Devices { get; set; } = new();

    /// <summary>
    /// v2 通信通道集合。通道是串口/TCP 共享资源的生命周期所有者。
    /// </summary>
    public List<ChannelEntry> Channels { get; set; } = new();

    /// <summary>
    /// 当前支持的配置版本号。
    /// </summary>
    public const int LegacySchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// 单个设备连接项。
    /// </summary>
    public sealed class DeviceEntry
    {
        /// <summary>
        /// 持久化设备身份，改名称不改变该值。
        /// </summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>
        /// 项目内唯一设备编码。
        /// </summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>
        /// 设备名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// v1 兼容字段；v2 驱动由 DriverKey 指定。
        /// </summary>
        public string Protocol { get; set; } = string.Empty;
        /// <summary>
        /// v1 兼容字段；v2 连接参数由 ChannelId 指向通道。
        /// </summary>
        public string Address { get; set; } = string.Empty;
        /// <summary>
        /// v2 所属通道身份。
        /// </summary>
        public string ChannelId { get; set; } = string.Empty;
        /// <summary>
        /// 编译期注册的驱动键，不把 PLC 型号混入点位协议枚举。
        /// </summary>
        public string DriverKey { get; set; } = string.Empty;
        /// <summary>
        /// 厂商和型号用于驱动 Profile 选择及现场支持矩阵。
        /// </summary>
        public string Manufacturer { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string CpuProfile { get; set; } = string.Empty;
        /// <summary>
        /// Modbus 从站号；非 Modbus 设备不使用。
        /// </summary>
        public int? ModbusUnitId { get; set; }
        /// <summary>
        /// 单设备轮询周期，单位毫秒。
        /// </summary>
        public int PollIntervalMs { get; set; } = 500;
        /// <summary>
        /// 点位超过该时间没有成功采样时标记为陈旧，单位毫秒。
        /// </summary>
        public int StaleAfterMs { get; set; }
        /// <summary>
        /// 是否启用该设备。
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// 校验配置，版本号或运行模式不合法时直接报错。
    /// </summary>
    public void Validate()
    {
        if (SchemaVersion == LegacySchemaVersion)
        {
            ValidateLegacy();
            return;
        }
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"device.json schemaVersion={SchemaVersion} 不受支持（支持 {LegacySchemaVersion} 迁移或当前版本 {CurrentSchemaVersion}）");
        // 运行模式只允许模拟或硬件。
        if (DeviceMode is not (DeviceMode.Simulation or DeviceMode.Hardware))
            throw new ConfigValidationException($"device.json deviceMode={DeviceMode} 非法");

        var issues = new List<ConfigurationIssue>();
        var channels = Channels ?? new List<ChannelEntry>();
        var devices = Devices ?? new List<DeviceEntry>();
        for (var index = 0; index < channels.Count; index++)
        {
            if (channels[index] is null)
            {
                issues.Add(new($"device.json.channels[{index}]", "不能为空"));
                continue;
            }
            issues.AddRange(channels[index].Validate($"device.json.channels[{index}]"));
        }

        var channelIds = channels.Where(channel => channel is not null).Select(channel => channel.Id).ToList();
        if (channelIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != channelIds.Count)
            issues.Add(new("device.json.channels", "存在重复通道 Id"));

        var channelCodes = channels.Where(channel => channel is not null)
            .Select(channel => channel.Code?.Trim() ?? string.Empty).ToList();
        if (channelCodes.Any(string.IsNullOrWhiteSpace)) issues.Add(new("device.json.channels.code", "不能为空"));
        if (channelCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != channelCodes.Count)
            issues.Add(new("device.json.channels.code", "存在重复通道编码"));

        var deviceCodes = devices.Where(device => device is not null)
            .Select(device => device.Code?.Trim() ?? string.Empty).ToList();
        if (deviceCodes.Any(string.IsNullOrWhiteSpace)) issues.Add(new("device.json.devices.code", "不能为空"));
        if (deviceCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != deviceCodes.Count)
            issues.Add(new("device.json.devices.code", "存在重复设备编码"));

        var deviceIds = devices.Where(device => device is not null).Select(device => device.Id).ToList();
        if (deviceIds.Any(id => !Guid.TryParse(id, out _))) issues.Add(new("device.json.devices.id", "必须是有效 GUID"));
        if (deviceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != deviceIds.Count)
            issues.Add(new("device.json.devices.id", "存在重复设备 Id"));

        foreach (var device in devices)
        {
            if (device is null)
            {
                issues.Add(new("device.json.devices", "存在空设备项"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(device.Name)) issues.Add(new($"device.json.devices[{device.Code}].name", "不能为空"));
            if (!channelIds.Contains(device.ChannelId, StringComparer.OrdinalIgnoreCase))
                issues.Add(new($"device.json.devices[{device.Code}].channelId", $"引用的通道不存在：{device.ChannelId}"));
            if (string.IsNullOrWhiteSpace(device.DriverKey))
                issues.Add(new($"device.json.devices[{device.Code}].driverKey", "不能为空"));
            if (device.PollIntervalMs <= 0)
                issues.Add(new($"device.json.devices[{device.Code}].pollIntervalMs", "必须大于 0"));
            if (device.StaleAfterMs <= 0)
                issues.Add(new($"device.json.devices[{device.Code}].staleAfterMs", "必须大于 0"));
        }

        var enabledSerialPorts = channels
            .Where(channel => channel is not null
                && channel.Enabled
                && channel.TransportKind == ChannelTransportKind.Serial
                && channel.Serial is not null)
            .GroupBy(channel => channel.Serial!.PortName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        foreach (var port in enabledSerialPorts)
            issues.Add(new("device.json.channels", $"启用通道重复占用串口：{port}"));

        if (issues.Count > 0)
            throw new ConfigValidationException(string.Join("；", issues));
    }

    private void ValidateLegacy()
    {
        // 运行模式只允许模拟或硬件。
        if (DeviceMode is not (DeviceMode.Simulation or DeviceMode.Hardware))
            throw new ConfigValidationException($"device.json deviceMode={DeviceMode} 非法");
    }
}
