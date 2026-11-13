using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Simulation;
using XXX.TestBench.Devices.Runtime;
using XXX.TestBench.Devices.Siemens;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices;

/// <summary>
/// 设备运行时工厂：根据一个完整、已版本化的配置快照创建运行时。
/// Simulation 使用仿真运行时；S7 Hardware 使用 S7NetPlus，未实现驱动仍明确失败，不得回退 Simulation。
/// </summary>
public sealed class DeviceRuntimeFactory : IDeviceRuntimeFactory
{
    /// <summary>
    /// 设备配置。
    /// </summary>
    private readonly DeviceConfigurationSnapshot _snapshot;
    /// <summary>
    /// 时间来源。
    /// </summary>
    private readonly IClock _clock;
    /// <summary>
    /// </summary>
    private readonly IS7PlcClientFactory? _s7ClientFactory;
    private readonly IDeviceEventSink? _events;
    private readonly IModbusClientFactory? _modbusClientFactory;
    private readonly IReadOnlyList<RequiredSignal> _requiredSignals;

    /// <summary>
    /// 创建运行时工厂。
    /// </summary>
    public DeviceRuntimeFactory(
        DeviceConfigurationSnapshot snapshot,
        IClock clock,
        IS7PlcClientFactory? s7ClientFactory = null,
        IDeviceEventSink? events = null,
        IModbusClientFactory? modbusClientFactory = null,
        IEnumerable<RequiredSignal>? requiredSignals = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _s7ClientFactory = s7ClientFactory;
        _events = events;
        _modbusClientFactory = modbusClientFactory;
        _requiredSignals = requiredSignals?.ToArray() ?? Array.Empty<RequiredSignal>();
    }

    /// <summary>
    /// 按当前设备配置创建设备运行时。
    /// </summary>
    public Task<IDeviceRuntime> CreateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_snapshot.Revision))
            throw new DomainException("创建设备运行时必须提供完整配置版本号");
        MultiDeviceConfigurationValidator.EnsureStructureValid(_snapshot);
        return Task.FromResult<IDeviceRuntime>(new MultiDeviceRuntime(_snapshot, _clock,
            s7ClientFactory: _s7ClientFactory,
            events: _events,
            modbusClientFactory: _modbusClientFactory,
            requiredSignals: _requiredSignals));
    }
}
