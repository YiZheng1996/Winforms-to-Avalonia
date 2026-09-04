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
    public int SchemaVersion { get; set; }
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
    /// 当前支持的配置版本号。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 单个设备连接项。
    /// </summary>
    public sealed class DeviceEntry
    {
        /// <summary>
        /// 设备名称。
        /// </summary>
        public required string Name { get; set; }
        /// <summary>
        /// 通信协议。
        /// </summary>
        public required string Protocol { get; set; }
        /// <summary>
        /// 通信地址。
        /// </summary>
        public required string Address { get; set; }
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
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"device.json schemaVersion={SchemaVersion} 不受支持（期望 {CurrentSchemaVersion}）");
        // 运行模式只允许模拟或硬件。
        if (DeviceMode is not (DeviceMode.Simulation or DeviceMode.Hardware))
            throw new ConfigValidationException($"device.json deviceMode={DeviceMode} 非法");
    }
}
