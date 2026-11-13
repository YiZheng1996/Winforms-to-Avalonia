using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.ViewModels.Process;

/// <summary>
/// 气路图只保存控件引用和管段显示状态，不创建第二套点位模型。
/// </summary>
public sealed partial class ProcessDiagramViewModel : ObservableObject
{
    private PipePressureState _supplyPipeState = PipePressureState.Unknown;
    private PipePressureState _mainPipeState = PipePressureState.Unknown;
    private PipePressureState _dutPipeState = PipePressureState.Unknown;
    private PipePressureState _exhaustPipeState = PipePressureState.Unknown;
    private bool _isDemoData;

    public ProcessDiagramViewModel(
        DigitalInputPointViewModel safetyDoor,
        DigitalInputPointViewModel clampReady,
        DigitalInputPointViewModel inletFeedback,
        DigitalInputPointViewModel exhaustFeedback,
        AnalogInputPointViewModel supplyPressure,
        AnalogInputPointViewModel mainPressure,
        AnalogInputPointViewModel dutPressure,
        DigitalOutputPointViewModel inletValve,
        DigitalOutputPointViewModel exhaustValve,
        AnalogOutputPointViewModel pressureSetpoint)
    {
        SafetyDoor = safetyDoor;
        ClampReady = clampReady;
        InletFeedback = inletFeedback;
        ExhaustFeedback = exhaustFeedback;
        SupplyPressure = supplyPressure;
        MainPressure = mainPressure;
        DutPressure = dutPressure;
        InletValve = inletValve;
        ExhaustValve = exhaustValve;
        PressureSetpoint = pressureSetpoint;
    }

    public DigitalInputPointViewModel SafetyDoor { get; }
    public DigitalInputPointViewModel ClampReady { get; }
    public DigitalInputPointViewModel InletFeedback { get; }
    public DigitalInputPointViewModel ExhaustFeedback { get; }
    public AnalogInputPointViewModel SupplyPressure { get; }
    public AnalogInputPointViewModel MainPressure { get; }
    public AnalogInputPointViewModel DutPressure { get; }
    public DigitalOutputPointViewModel InletValve { get; }
    public DigitalOutputPointViewModel ExhaustValve { get; }
    public AnalogOutputPointViewModel PressureSetpoint { get; }

    public PipePressureState SupplyPipeState
    {
        get => _supplyPipeState;
        private set => SetProperty(ref _supplyPipeState, value);
    }

    public PipePressureState MainPipeState
    {
        get => _mainPipeState;
        private set => SetProperty(ref _mainPipeState, value);
    }

    public PipePressureState DutPipeState
    {
        get => _dutPipeState;
        private set => SetProperty(ref _dutPipeState, value);
    }

    public PipePressureState ExhaustPipeState
    {
        get => _exhaustPipeState;
        private set => SetProperty(ref _exhaustPipeState, value);
    }

    public bool IsDemoData
    {
        get => _isDemoData;
        private set => SetProperty(ref _isDemoData, value);
    }

    public PipePressureState DisplaySupplyPipeState
        => IsDemoData ? PipePressureState.Pressurized : SupplyPipeState;

    public PipePressureState DisplayMainPipeState
        => IsDemoData ? PipePressureState.Pressurized : MainPipeState;

    public PipePressureState DisplayDutPipeState
        => IsDemoData ? PipePressureState.Pressurized : DutPipeState;

    public PipePressureState DisplayExhaustPipeState
        => IsDemoData ? PipePressureState.Unpressurized : ExhaustPipeState;

    public string ExhaustPipeText => "排放段 · 未配置压力测点";

    public void ResetPressureStates()
    {
        SupplyPipeState = PipePressureState.Unknown;
        MainPipeState = PipePressureState.Unknown;
        DutPipeState = PipePressureState.Unknown;
        ExhaustPipeState = PipePressureState.Unknown;
        NotifyDisplayedPipeStates();
    }

    public void UpdatePressureStates()
    {
        SupplyPipeState = Evaluate(SupplyPressure, SupplyPipeState);
        MainPipeState = Evaluate(MainPressure, MainPipeState);
        DutPipeState = Evaluate(DutPressure, DutPipeState);
        // 排气消音器后没有压力测点，不能由阀命令或上游压力推导。
        ExhaustPipeState = PipePressureState.Unknown;
        NotifyDisplayedPipeStates();
    }

    public void SetDemoData(bool enabled)
    {
        if (IsDemoData == enabled)
            return;
        IsDemoData = enabled;
        NotifyDisplayedPipeStates();
    }

    private void NotifyDisplayedPipeStates()
    {
        OnPropertyChanged(nameof(DisplaySupplyPipeState));
        OnPropertyChanged(nameof(DisplayMainPipeState));
        OnPropertyChanged(nameof(DisplayDutPipeState));
        OnPropertyChanged(nameof(DisplayExhaustPipeState));
    }

    private static PipePressureState Evaluate(
        AnalogInputPointViewModel point,
        PipePressureState previous)
    {
        if (point.State != ProcessDataState.Good || !point.Value.HasValue)
            return PipePressureState.Unknown;

        const double enterThreshold = 0.020;
        const double exitThreshold = 0.010;
        return previous switch
        {
            PipePressureState.Pressurized when point.Value.Value > exitThreshold
                => PipePressureState.Pressurized,
            PipePressureState.Unpressurized when point.Value.Value >= enterThreshold
                => PipePressureState.Pressurized,
            PipePressureState.Unpressurized => PipePressureState.Unpressurized,
            PipePressureState.Pressurized => PipePressureState.Unpressurized,
            _ when point.Value.Value >= enterThreshold => PipePressureState.Pressurized,
            _ when point.Value.Value <= exitThreshold => PipePressureState.Unpressurized,
            _ => PipePressureState.Unknown
        };
    }
}
