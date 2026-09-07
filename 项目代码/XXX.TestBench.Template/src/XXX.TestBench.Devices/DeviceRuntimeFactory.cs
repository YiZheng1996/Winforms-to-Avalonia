using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Simulation;
using XXX.TestBench.Devices.Runtime;

namespace XXX.TestBench.Devices;

/// <summary>
/// 设备运行时工厂：Simulation 创建仿真运行时；Hardware 阶段 5 前没有适配器时必须失败，
/// 不得回退 Simulation、不得伪造成功数据。
/// </summary>
public sealed class DeviceRuntimeFactory : IDeviceRuntimeFactory
{
    /// <summary>
    /// 设备配置。
    /// </summary>
    private readonly DeviceConfig _deviceConfig;
    /// <summary>
    /// 点位配置。
    /// </summary>
    private readonly PointsConfig _pointsConfig;
    /// <summary>
    /// 模拟运行配置。
    /// </summary>
    private readonly SimulationConfig _simulationConfig;
    /// <summary>
    /// 时间来源。
    /// </summary>
    private readonly IClock _clock;
    /// <summary>
    /// 当前完整配置快照版本；旧版运行时使用兼容默认值。
    /// </summary>
    private readonly string _revision;
    /// <summary>
    /// 当前完整配置中的项目级业务信号绑定。
    /// </summary>
    private readonly SignalBindingsConfig _signalBindings;

    /// <summary>
    /// 创建运行时工厂。
    /// </summary>
    public DeviceRuntimeFactory(
        DeviceConfig deviceConfig,
        PointsConfig pointsConfig,
        SimulationConfig simulationConfig,
        IClock clock,
        string? revision = null,
        SignalBindingsConfig? signalBindings = null)
    {
        _deviceConfig = deviceConfig;
        _pointsConfig = pointsConfig;
        _simulationConfig = simulationConfig;
        _clock = clock;
        _revision = string.IsNullOrWhiteSpace(revision) ? "runtime-v2" : revision.Trim();
        _signalBindings = signalBindings ?? new SignalBindingsConfig();
    }

    /// <summary>
    /// 按模式创建设备运行时。
    /// </summary>
    public Task<IDeviceRuntime> CreateAsync(DeviceMode mode, CancellationToken ct = default)
    {
        if (mode == DeviceMode.Simulation)
        {
            if (_deviceConfig.SchemaVersion == DeviceConfig.CurrentSchemaVersion
                && _pointsConfig.SchemaVersion == PointsConfig.CurrentSchemaVersion
                && _simulationConfig.SchemaVersion == SimulationConfig.CurrentSchemaVersion)
            {
                var snapshot = new DeviceConfigurationSnapshot
                {
                    Revision = _revision,
                    Device = _deviceConfig,
                    Points = _pointsConfig,
                    Simulation = _simulationConfig,
                    SignalBindings = _signalBindings
                };
                return Task.FromResult<IDeviceRuntime>(new MultiDeviceRuntime(snapshot, _clock));
            }
            var points = _pointsConfig.Points.Select(p => p.ToDomain()).ToList();
            var device = _deviceConfig.Devices.FirstOrDefault(d => d.Enabled) ?? throw new DomainException("device.json 没有启用的仿真设备");
            return Task.FromResult<IDeviceRuntime>(new SimulationDeviceRuntime(device.Name, points, _simulationConfig, _clock));
        }

        // Hardware：未接入适配器前明确失败，保留 Hardware 模式并进入 Faulted
        throw new DomainException("Hardware 模式尚无可用设备适配器（阶段 5），拒绝创建运行时");
    }
}
