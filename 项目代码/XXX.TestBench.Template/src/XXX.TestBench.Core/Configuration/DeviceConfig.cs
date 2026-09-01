using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

public sealed class DeviceConfig
{
    public int SchemaVersion { get; set; }
    public DeviceMode DeviceMode { get; set; }
    public int PollIntervalMs { get; set; } = 500;
    public int TimeoutMs { get; set; } = 1000;
    public List<DeviceEntry> Devices { get; set; } = new();

    public const int CurrentSchemaVersion = 1;

    public sealed class DeviceEntry
    {
        public required string Name { get; set; }
        public required string Protocol { get; set; }
        public required string Address { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"device.json schemaVersion={SchemaVersion} 不受支持（期望 {CurrentSchemaVersion}）");
        if (DeviceMode is not (DeviceMode.Simulation or DeviceMode.Hardware))
            throw new ConfigValidationException($"device.json deviceMode={DeviceMode} 非法");
    }
}
