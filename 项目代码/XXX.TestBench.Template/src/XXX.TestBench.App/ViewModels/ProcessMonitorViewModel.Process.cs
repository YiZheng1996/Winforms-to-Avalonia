using XXX.TestBench.App.ViewModels.Process;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class ProcessMonitorViewModel
{
    private readonly Dictionary<string, ProcessPointViewModel> _processPoints =
        new(StringComparer.OrdinalIgnoreCase);

    public DigitalInputPointViewModel SafetyDoor { get; private set; } = null!;
    public DigitalInputPointViewModel ClampReady { get; private set; } = null!;
    public AnalogInputPointViewModel SupplyPressure { get; private set; } = null!;
    public AnalogInputPointViewModel MainPressure { get; private set; } = null!;
    public AnalogInputPointViewModel DutPressure { get; private set; } = null!;
    public AnalogInputPointViewModel PressureSetpointReadback { get; private set; } = null!;
    public DigitalInputPointViewModel InletFeedback { get; private set; } = null!;
    public DigitalInputPointViewModel ExhaustFeedback { get; private set; } = null!;
    public DigitalOutputPointViewModel InletValve { get; private set; } = null!;
    public DigitalOutputPointViewModel ExhaustValve { get; private set; } = null!;
    public AnalogOutputPointViewModel PressureSetpoint { get; private set; } = null!;
    public ProcessDiagramViewModel Diagram { get; private set; } = null!;

    public bool CanManageProcessBindings
        => _actor.HasPermission(PermissionCode.ManageDevices)
            && _services.DeviceConfigurations is not null;

    public string RuntimeStatusText
        => _services.DeviceModes.Runtime is null
            ? "设备运行时未初始化"
            : _services.DeviceModes.Health switch
            {
                DeviceHealth.Healthy => "设备在线",
                DeviceHealth.Degraded => "设备部分在线",
                DeviceHealth.Faulted => "设备故障",
                DeviceHealth.Unknown => "设备状态未知",
                _ => "设备未连接"
            };

    private void BuildProcessPointModels()
    {
        SafetyDoor = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.SafetyDoor));
        ClampReady = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.ClampReady));
        SupplyPressure = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.SupplyPressure));
        MainPressure = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.MainPressure));
        DutPressure = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.DutPressure));
        PressureSetpointReadback = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.PressureSetpointReadback));
        InletFeedback = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.InletFeedback));
        ExhaustFeedback = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.ExhaustFeedback));
        InletValve = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.InletCommand));
        ExhaustValve = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.ExhaustCommand));
        PressureSetpoint = new(ProcessSignalCatalog.Get(ProcessSignalCatalog.PressureSetpoint));

        InletValve.AttachFeedback(InletFeedback);
        ExhaustValve.AttachFeedback(ExhaustFeedback);
        Diagram = new ProcessDiagramViewModel(
            SafetyDoor,
            ClampReady,
            InletFeedback,
            ExhaustFeedback,
            SupplyPressure,
            MainPressure,
            DutPressure,
            InletValve,
            ExhaustValve);

        foreach (var point in new ProcessPointViewModel[]
        {
            SafetyDoor, ClampReady, SupplyPressure, MainPressure, DutPressure,
            PressureSetpointReadback, InletFeedback, ExhaustFeedback,
            InletValve, ExhaustValve, PressureSetpoint
        })
            _processPoints[point.SignalKey] = point;

        InletValve.AttachCommands(OpenInletCommand, CloseInletCommand);
        ExhaustValve.AttachCommands(OpenExhaustCommand, CloseExhaustCommand);
        PressureSetpoint.AttachApplyCommand(ApplyPressureSetpointCommand);
        InletValve.PropertyChanged += (_, _) =>
        {
            OpenInletCommand.NotifyCanExecuteChanged();
            CloseInletCommand.NotifyCanExecuteChanged();
        };
        ExhaustValve.PropertyChanged += (_, _) =>
        {
            OpenExhaustCommand.NotifyCanExecuteChanged();
            CloseExhaustCommand.NotifyCanExecuteChanged();
        };
        PressureSetpoint.PropertyChanged += (_, _) =>
            ApplyPressureSetpointCommand.NotifyCanExecuteChanged();
    }

    private void ApplyProcessBindings(
        IDeviceRuntime runtime,
        IReadOnlyList<DevicePoint> points)
    {
        var pointById = points
            .Where(point => !string.IsNullOrWhiteSpace(point.PointId))
            .GroupBy(point => point.PointId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var bindings = runtime.SignalBindings?.Bindings
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in ProcessSignalCatalog.All)
        {
            if (!_processPoints.TryGetValue(definition.SignalKey, out var viewModel))
                continue;
            bindings.TryGetValue(definition.SignalKey, out var pointId);
            pointId = pointId?.Trim();
            pointById.TryGetValue(pointId ?? string.Empty, out var point);
            var device = point is null
                ? null
                : DeviceStationDisplay.ResolveDevice(_services.DeviceConfig.Devices, point.DeviceId);
            var deviceText = DeviceStationDisplay.FormatDeviceText(device);
            var isSimulation = point is not null && IsSimulationDevice(runtime, point.DeviceId);
            viewModel.ApplyBinding(point, deviceText, isSimulation);
        }

        Diagram.ResetPressureStates();
        OnPropertyChanged(nameof(RuntimeStatusText));
        OnPropertyChanged(nameof(CanManageProcessBindings));
    }

    private static bool IsSimulationDevice(IDeviceRuntime runtime, string deviceId)
    {
        try { return runtime.GetDeviceStatus(deviceId).IsSimulation; }
        catch (Exception) { return runtime.IsSimulation; }
    }

    public ProcessPointBindingsDialogViewModel CreateProcessBindingsDialog()
    {
        var runtime = _services.DeviceModes.Runtime;
        var bindings = runtime?.SignalBindings?.Bindings
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var revision = runtime?.ActiveRevision ?? string.Empty;
        return new ProcessPointBindingsDialogViewModel(
            ProcessSignalCatalog.All,
            _services.DevicePoints.List(),
            _services.DeviceConfig.Devices,
            bindings,
            revision,
            _services.DeviceConfigurations
                ?? throw new Core.Common.DomainException("当前设备配置服务未连接"),
            _actor,
            runtime);
    }
}
