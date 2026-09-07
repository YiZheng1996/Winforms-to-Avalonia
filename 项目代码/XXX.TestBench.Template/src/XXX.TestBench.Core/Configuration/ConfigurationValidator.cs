using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 跨配置一致性校验：Hardware 必须有启用设备；Simulation 初值/规则/故障注入地址必须存在于 points.json。
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>
    /// 校验全部配置及其交叉一致性。
    /// </summary>
    public static void ValidateAll(AppConfig app, DeviceConfig device, PointsConfig points, SimulationConfig simulation)
    {
        app.Validate();
        device.Validate();
        points.Validate();
        simulation.Validate();

        if (device.SchemaVersion == DeviceConfig.CurrentSchemaVersion
            || points.SchemaVersion == PointsConfig.CurrentSchemaVersion
            || simulation.SchemaVersion == SimulationConfig.CurrentSchemaVersion)
        {
            if (device.SchemaVersion != DeviceConfig.CurrentSchemaVersion
                || points.SchemaVersion != PointsConfig.CurrentSchemaVersion
                || simulation.SchemaVersion != SimulationConfig.CurrentSchemaVersion)
                throw new ConfigValidationException("设备、点位和仿真配置版本不一致，不能混用 v1/v2 运行");
            MultiDeviceConfigurationValidator.EnsureValid(new DeviceConfigurationSnapshot
            {
                Revision = "legacy-validator",
                Device = device,
                Points = points,
                Simulation = simulation,
                SignalBindings = new SignalBindingsConfig()
            });
            return;
        }

        // 硬件模式必须至少启用一台设备。
        if (device.DeviceMode == DeviceMode.Hardware
            && !(device.Devices ?? new List<DeviceConfig.DeviceEntry>()).Any(d => d is not null && d.Enabled))
            throw new ConfigValidationException("Hardware 模式必须配置至少一个启用的设备");

        // 模拟模式的初值、变化规则与故障注入地址必须存在于点位表中。
        if (device.DeviceMode == DeviceMode.Simulation)
        {
            var addresses = (points.Points ?? new List<PointsConfig.PointEntry>())
                .Where(p => p is not null)
                .Where(p => p.IsEnabled && p.ProtocolKind == DevicePointProtocol.Simulation)
                .Select(p => p.Address)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in (simulation.InitialValues ?? new Dictionary<string, object?>()).Keys)
                if (!addresses.Contains(key))
                    throw new ConfigValidationException($"simulation.json 初值地址 {key} 不在 points.json");

            foreach (var rule in simulation.ChangeRules ?? new List<SimulationConfig.ChangeRule>())
                if (!addresses.Contains(rule.Address))
                    throw new ConfigValidationException($"simulation.json 变化规则地址 {rule.Address} 不在 points.json");

            foreach (var scenario in simulation.FaultInjectionScenarios ?? new List<SimulationConfig.FaultInjection>())
                if (!addresses.Contains(scenario.Address))
                    throw new ConfigValidationException($"simulation.json 故障注入地址 {scenario.Address} 不在 points.json");
        }
    }
}
