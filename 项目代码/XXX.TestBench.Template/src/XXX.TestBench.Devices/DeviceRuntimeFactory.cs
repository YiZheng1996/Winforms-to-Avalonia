using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Simulation;

namespace XXX.TestBench.Devices;

/// <summary>
/// 设备运行时工厂：Simulation 创建仿真运行时；Hardware 阶段 5 前没有适配器时必须失败，
/// 不得回退 Simulation、不得伪造成功数据。
/// </summary>
public sealed class DeviceRuntimeFactory : IDeviceRuntimeFactory
{
    private readonly DeviceConfig _deviceConfig;
    private readonly PointsConfig _pointsConfig;
    private readonly SimulationConfig _simulationConfig;
    private readonly IClock _clock;

    public DeviceRuntimeFactory(DeviceConfig deviceConfig, PointsConfig pointsConfig, SimulationConfig simulationConfig, IClock clock)
    {
        _deviceConfig = deviceConfig;
        _pointsConfig = pointsConfig;
        _simulationConfig = simulationConfig;
        _clock = clock;
    }

    public Task<IDeviceRuntime> CreateAsync(DeviceMode mode, CancellationToken ct = default)
    {
        if (mode == DeviceMode.Simulation)
        {
            var points = _pointsConfig.Points.Select(p => p.ToDomain()).ToList();
            var device = _deviceConfig.Devices.FirstOrDefault(d => d.Enabled) ?? throw new DomainException("device.json 没有启用的仿真设备");
            return Task.FromResult<IDeviceRuntime>(new SimulationDeviceRuntime(device.Name, points, _simulationConfig, _clock));
        }

        // Hardware：未接入适配器前明确失败，保留 Hardware 模式并进入 Faulted
        throw new DomainException("Hardware 模式尚无可用设备适配器（阶段 5），拒绝创建运行时");
    }
}
