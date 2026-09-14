using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using System.Text.Json.Serialization;

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
    /// 旧版项目级运行模式。当前版本不再使用，运行模式由每台设备单独维护。
    /// 仅保留该属性以便读取历史对象；当前配置序列化时不会写出。
    /// </summary>
    [JsonIgnore]
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
    public const int PreviousSchemaVersion = 3;
    public const int CurrentSchemaVersion = 4;

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
        /// 当前设备的运行模式。仿真只影响本设备，不再由项目级开关统一决定。
        /// </summary>
        public DeviceMode DeviceMode { get; set; } = DeviceMode.Simulation;
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
        /// 驱动定义的设备系列键。只能从驱动公布的固定选项中选择。
        /// </summary>
        public string Model { get; set; } = string.Empty;
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
        /// 采集策略；按需模式不会启动后台轮询。
        /// </summary>
        public DeviceScanMode ScanMode { get; set; } = DeviceScanMode.FixedInterval;
        /// <summary>
        /// 单设备连接、请求和重试参数。
        /// </summary>
        public DeviceTimingOptions Timing { get; set; } = new();
        /// <summary>
        /// 连续失败后的设备级降级策略。
        /// </summary>
        public DeviceDemotionOptions AutoDemotion { get; set; } = new();
        /// <summary>
        /// Siemens S7 的设备专属连接端点；不与共享通道目标地址混用。
        /// </summary>
        public SiemensS7ConnectionOptions? SiemensS7 { get; set; }
        /// <summary>
        /// Modbus TCP 的设备专属连接端点；Modbus RTU 不使用该字段。
        /// </summary>
        public ModbusTcpConnectionOptions? ModbusTcp { get; set; }
        /// <summary>
        /// 旧版设备启用标记。当前版本创建后即参与运行，不再提供启用/停用开关。
        /// </summary>
        [JsonIgnore]
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
        if (SchemaVersion != PreviousSchemaVersion && SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"device.json schemaVersion={SchemaVersion} 不受支持（支持 {LegacySchemaVersion} 迁移或当前版本 {CurrentSchemaVersion}）");
        var isV4 = SchemaVersion == CurrentSchemaVersion;
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
            var hasLegacyTargetEndpoint = isV4
                && channels[index].Tcp is not null
                && (!string.IsNullOrWhiteSpace(channels[index].Tcp!.Host) || channels[index].Tcp!.Port != 0)
                && devices.Any(device => device is not null && device.SiemensS7 is null);
            issues.AddRange(channels[index].Validate($"device.json.channels[{index}]",
                allowLegacyTargetEndpoint: !isV4 || hasLegacyTargetEndpoint));
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
            if (device.DeviceMode is not (DeviceMode.Simulation or DeviceMode.Hardware))
                issues.Add(new($"device.json.devices[{device.Code}].deviceMode", $"运行模式非法：{device.DeviceMode}"));
            if (!channelIds.Contains(device.ChannelId, StringComparer.OrdinalIgnoreCase))
                issues.Add(new($"device.json.devices[{device.Code}].channelId", $"引用的通道不存在：{device.ChannelId}"));
            if (string.IsNullOrWhiteSpace(device.DriverKey))
                issues.Add(new($"device.json.devices[{device.Code}].driverKey", "不能为空"));
            if (device.PollIntervalMs <= 0)
                issues.Add(new($"device.json.devices[{device.Code}].pollIntervalMs", "必须大于 0"));
            if (device.StaleAfterMs <= 0)
                issues.Add(new($"device.json.devices[{device.Code}].staleAfterMs", "必须大于 0"));
            if (isV4)
            {
                if (device.ScanMode is not (DeviceScanMode.FixedInterval or DeviceScanMode.OnDemand))
                    issues.Add(new($"device.json.devices[{device.Code}].scanMode", $"采集策略非法：{device.ScanMode}"));
                var timing = device.Timing ?? new DeviceTimingOptions();
                issues.AddRange(timing.Validate($"device.json.devices[{device.Code}].timing"));
                issues.AddRange((device.AutoDemotion ?? new()).Validate($"device.json.devices[{device.Code}].autoDemotion"));
                var minimumStale = Math.Max(device.PollIntervalMs * 2, timing.RequestTimeoutMs + 100);
                if (device.StaleAfterMs < minimumStale)
                    issues.Add(new($"device.json.devices[{device.Code}].staleAfterMs", $"必须不小于 {minimumStale} ms"));

                if (string.Equals(device.DriverKey, DriverKeyCatalog.SiemensS7, StringComparison.OrdinalIgnoreCase))
                {
                    var legacyEndpointAvailable = !string.IsNullOrWhiteSpace(device.Address)
                        || channels.FirstOrDefault(channel => string.Equals(channel.Id, device.ChannelId, StringComparison.OrdinalIgnoreCase))?.Tcp is
                           { Host: { Length: > 0 }, Port: > 0 };
                    if (device.SiemensS7 is null && !legacyEndpointAvailable)
                        issues.Add(new($"device.json.devices[{device.Code}].siemensS7", "硬件 Siemens S7 必须配置设备专属端点"));
                    else if (device.SiemensS7 is not null)
                        issues.AddRange(device.SiemensS7.Validate($"device.json.devices[{device.Code}].siemensS7"));
                }

                ValidateModbusDevice(device, issues);
            }
        }

        ValidateModbusUniqueness(devices, issues);

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

    /// <summary>
    /// 校验 Modbus 设备的设备专属端点、站号以及同资源唯一性。
    ///
    /// 这里故意只做配置层校验，不尝试打开 TCP/串口，也不根据地址猜测数据类型。
    /// 驱动描述器还会继续校验通道、点位地址和点位数据类型。
    /// </summary>
    private static void ValidateModbusDevice(
        DeviceEntry device,
        ICollection<ConfigurationIssue> issues)
    {
        var isRtu = string.Equals(device.DriverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase);
        var isTcp = string.Equals(device.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase);
        if (!isRtu && !isTcp)
        {
            if (device.ModbusTcp is not null)
                issues.Add(new($"device.json.devices[{device.Code}].modbusTcp", "只有 Modbus TCP 设备可以配置 TCP 端点"));
            if (device.ModbusUnitId.HasValue)
                issues.Add(new($"device.json.devices[{device.Code}].modbusUnitId", "非 Modbus 设备不能配置站号"));
            return;
        }

        if (!device.ModbusUnitId.HasValue || device.ModbusUnitId.Value is < 1 or > 247)
            issues.Add(new($"device.json.devices[{device.Code}].modbusUnitId", "必须在 1-247 范围内，首版不支持广播站号 0"));

        if (isRtu && device.ModbusTcp is not null)
            issues.Add(new($"device.json.devices[{device.Code}].modbusTcp", "Modbus RTU 不能配置 Modbus TCP 端点"));
        if (isTcp)
        {
            if (device.DeviceMode == DeviceMode.Hardware && device.ModbusTcp is null)
                issues.Add(new($"device.json.devices[{device.Code}].modbusTcp", "Modbus TCP 必须配置设备专属端点"));
            else
            {
                if (device.ModbusTcp is not null)
                    foreach (var issue in device.ModbusTcp.Validate($"device.json.devices[{device.Code}].modbusTcp"))
                        issues.Add(issue);
            }
        }

    }

    /// <summary>
    /// 校验共享资源上的站号路由唯一性。设备模式即使是 Simulation 也保留静态校验，
    /// 这样切换到 Hardware 时不会因为重复站号才暴露配置问题。
    /// </summary>
    private static void ValidateModbusUniqueness(
        IReadOnlyList<DeviceEntry> devices,
        ICollection<ConfigurationIssue> issues)
    {
        var rtuGroups = devices
            .Where(device => device is not null
                && string.Equals(device.DriverKey, DriverKeyCatalog.ModbusRtu, StringComparison.OrdinalIgnoreCase)
                && device.ModbusUnitId.HasValue)
            .GroupBy(device => $"{device.ChannelId.Trim()}\u001f{device.ModbusUnitId.GetValueOrDefault()}", StringComparer.OrdinalIgnoreCase);
        foreach (var group in rtuGroups.Where(group => group.Count() > 1))
            foreach (var device in group)
                issues.Add(new($"device.json.devices[{device.Code}].modbusUnitId", "同一串口通道下 Modbus RTU 站号不能重复"));

        var tcpGroups = devices
            .Where(device => device is not null
                && string.Equals(device.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase)
                && device.ModbusTcp is not null
                && device.ModbusUnitId.HasValue)
            .GroupBy(device =>
                $"{device.ModbusTcp!.Host.Trim().ToUpperInvariant()}\u001f{device.ModbusTcp.Port}\u001f{device.ModbusUnitId.GetValueOrDefault()}",
                StringComparer.OrdinalIgnoreCase);
        foreach (var group in tcpGroups.Where(group => group.Count() > 1))
            foreach (var device in group)
                issues.Add(new($"device.json.devices[{device.Code}].modbusUnitId", "同一 TCP 地址和站号下 Modbus TCP 设备不能重复"));
    }

    private void ValidateLegacy()
    {
        // 旧版对象只做最小结构校验，当前版本不再读取项目级模式。
        if (DeviceMode is not (DeviceMode.Simulation or DeviceMode.Hardware))
            throw new ConfigValidationException($"device.json deviceMode={DeviceMode} 非法");
    }
}
