using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Simulation;
using XXX.TestBench.Devices.Runtime;
using XXX.TestBench.Devices.Siemens;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices;

/// <summary>
/// 设备运行时工厂：每台设备按照自己的 DeviceMode 创建会话。
/// Simulation 使用仿真运行时；S7 Hardware 使用 S7NetPlus，未实现驱动仍明确失败，不得回退 Simulation。
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
    private readonly IS7PlcClientFactory? _s7ClientFactory;
    private readonly IDeviceEventSink? _events;
    private readonly IModbusClientFactory? _modbusClientFactory;

    /// <summary>
    /// 创建运行时工厂。
    /// </summary>
    public DeviceRuntimeFactory(
        DeviceConfig deviceConfig,
        PointsConfig pointsConfig,
        SimulationConfig simulationConfig,
        IClock clock,
        string? revision = null,
        SignalBindingsConfig? signalBindings = null,
        IS7PlcClientFactory? s7ClientFactory = null,
        IDeviceEventSink? events = null,
        IModbusClientFactory? modbusClientFactory = null)
    {
        _deviceConfig = deviceConfig;
        _pointsConfig = pointsConfig;
        _simulationConfig = simulationConfig;
        _clock = clock;
        _revision = string.IsNullOrWhiteSpace(revision) ? "runtime-v2" : revision.Trim();
        _signalBindings = signalBindings ?? new SignalBindingsConfig();
        _s7ClientFactory = s7ClientFactory;
        _events = events;
        _modbusClientFactory = modbusClientFactory;
    }

    /// <summary>
    /// 按当前设备配置创建设备运行时。
    /// </summary>
    public Task<IDeviceRuntime> CreateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_deviceConfig.SchemaVersion != DeviceConfig.CurrentSchemaVersion
            && _deviceConfig.SchemaVersion != DeviceConfig.PreviousSchemaVersion
            || _pointsConfig.SchemaVersion != PointsConfig.CurrentSchemaVersion
            || _simulationConfig.SchemaVersion != SimulationConfig.CurrentSchemaVersion)
            throw new DomainException("设备、点位和仿真配置必须使用当前版本，不能回退旧运行模式");

        var snapshot = new DeviceConfigurationSnapshot
        {
            Revision = _revision,
            Device = _deviceConfig,
            Points = _pointsConfig,
            Simulation = _simulationConfig,
            SignalBindings = _signalBindings
        };
        return Task.FromResult<IDeviceRuntime>(new MultiDeviceRuntime(snapshot, _clock,
            s7ClientFactory: _s7ClientFactory,
            events: _events,
            modbusClientFactory: _modbusClientFactory));
    }

    /// <summary>
    /// 兼容旧接口的显式模式调用。全局模式已移除，硬件模式调用必须改为设备级配置。
    /// </summary>
    public Task<IDeviceRuntime> CreateAsync(DeviceMode mode, CancellationToken ct = default)
    {
        if (mode == DeviceMode.Hardware)
            throw new DomainException("设备运行模式已改为按设备配置，不能使用全局硬件模式");
        if (mode != DeviceMode.Simulation)
            throw new DomainException($"不支持的全局设备运行模式：{mode}");
        return CreateAsync(ct);
    }
}
