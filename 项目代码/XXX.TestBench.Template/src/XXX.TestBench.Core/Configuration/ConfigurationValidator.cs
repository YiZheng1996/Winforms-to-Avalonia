using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

/// <summary>跨配置一致性校验：Hardware 必须有启用设备；Simulation 初值/规则/故障注入地址必须存在于 points.json。</summary>
public static class ConfigurationValidator
{
    public static void ValidateAll(AppConfig app, DeviceConfig device, PointsConfig points, SimulationConfig simulation)
    {
        app.Validate();
        device.Validate();
        points.Validate();
        simulation.Validate();

        if (device.DeviceMode == DeviceMode.Hardware && !device.Devices.Any(d => d.Enabled))
            throw new ConfigValidationException("Hardware 模式必须配置至少一个启用的设备");

        if (device.DeviceMode == DeviceMode.Simulation)
        {
            var addresses = points.Points
                .Where(p => p.Protocol.Equals("Simulation", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Address)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in simulation.InitialValues.Keys)
                if (!addresses.Contains(key))
                    throw new ConfigValidationException($"simulation.json 初值地址 {key} 不在 points.json");

            foreach (var rule in simulation.ChangeRules)
                if (!addresses.Contains(rule.Address))
                    throw new ConfigValidationException($"simulation.json 变化规则地址 {rule.Address} 不在 points.json");

            foreach (var scenario in simulation.FaultInjectionScenarios)
                if (!addresses.Contains(scenario.Address))
                    throw new ConfigValidationException($"simulation.json 故障注入地址 {scenario.Address} 不在 points.json");
        }
    }
}
