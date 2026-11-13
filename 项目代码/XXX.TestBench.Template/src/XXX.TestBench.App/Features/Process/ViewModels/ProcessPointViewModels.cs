using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels.Process;

public enum ProcessLimitState
{
    Unknown,
    Normal,
    Low,
    High,
    Invalid
}

public sealed record ProcessTrendSample(DateTime? TimestampUtc, double? Value);

/// <summary>
/// 工艺页面的点位显示基类。只持有已解析的点位元数据，不访问设备和配置存储。
/// </summary>
public abstract class ProcessPointViewModel : ObservableObject
{
    private string _pointId = string.Empty;
    private string _deviceName = "未分配设备";
    private bool _isSimulation;
    private ProcessDataState _state = ProcessDataState.Unbound;
    private string _statusText = "未绑定";
    private DateTime? _timestampUtc;
    private DevicePoint? _point;
    private DateTime? _lastAcceptedTimestampUtc;
    private bool _isDemoData;

    protected ProcessPointViewModel(ProcessSignalDefinition definition)
    {
        Definition = definition;
    }

    public ProcessSignalDefinition Definition { get; }
    public string SignalKey => Definition.SignalKey;
    public string DisplayName => Definition.DisplayName;
    public string Unit => Definition.Unit;
    public bool IsWritable => Definition.IsWritable;
    public bool IsBound => !string.IsNullOrWhiteSpace(PointId);
    public string PointId
    {
        get => _pointId;
        private set => SetProperty(ref _pointId, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    public bool IsSimulation
    {
        get => _isSimulation;
        private set => SetProperty(ref _isSimulation, value);
    }

    public ProcessDataState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(StatusBrushKey));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        protected set => SetProperty(ref _statusText, value);
    }

    public DateTime? TimestampUtc
    {
        get => _timestampUtc;
        private set => SetProperty(ref _timestampUtc, value);
    }

    public bool IsValid => State == ProcessDataState.Good;

    /// <summary>
    /// 演示数据只用于明确开启后的视觉预览，不改变真实状态、点位绑定或输出安全判断。
    /// </summary>
    public bool IsDemoData
    {
        get => _isDemoData;
        private set
        {
            if (!SetProperty(ref _isDemoData, value))
                return;
            OnPropertyChanged(nameof(DisplayedState));
            OnPropertyChanged(nameof(DisplayStatusText));
            OnPropertyChanged(nameof(DisplayPointText));
            OnDemoModeChanged(value);
        }
    }

    public ProcessDataState DisplayedState => IsDemoData ? ProcessDataState.Good : State;

    public string DisplayStatusText => IsDemoData ? "正常" : StatusText;

    public string StatusBrushKey => State switch
    {
        ProcessDataState.Good => "Good",
        ProcessDataState.Disconnected => "Bad",
        ProcessDataState.Bad or ProcessDataState.InvalidBinding or ProcessDataState.Stale => "Bad",
        _ => "Unknown"
    };

    public string PointText
    {
        get
        {
            if (!IsBound)
                return "未绑定";
            var pointName = Point?.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(pointName))
                return pointName;
            var pointCode = Point?.Code?.Trim();
            return string.IsNullOrWhiteSpace(pointCode) ? PointId : pointCode;
        }
    }

    public string DisplayPointText => IsDemoData ? "演示数据" : PointText;
    public string SourceText => IsBound ? DeviceName : "未绑定";

    public DevicePoint? Point => _point;

    public virtual void ApplyBinding(DevicePoint? point, string? deviceName, bool isSimulation)
    {
        _point = point;
        _lastAcceptedTimestampUtc = null;
        PointId = point?.PointId?.Trim() ?? string.Empty;
        DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "未分配设备" : deviceName.Trim();
        IsSimulation = isSimulation;
        OnPropertyChanged(nameof(IsBound));
        OnPropertyChanged(nameof(PointText));
        OnPropertyChanged(nameof(SourceText));
        SetState(string.IsNullOrWhiteSpace(PointId)
            ? ProcessDataState.Unbound
            : point is null ? ProcessDataState.InvalidBinding : ProcessDataState.Waiting,
            null);
    }

    public void SetDemoData(bool enabled)
        => IsDemoData = enabled;

    protected virtual void OnDemoModeChanged(bool enabled)
    {
    }

    public virtual void ApplySample(ProcessSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!IsSampleForCurrentPoint(sample))
            return;

        var effectiveSample = sample;
        if (IsTimestampRegression(sample.TimestampUtc))
        {
            effectiveSample = sample with
            {
                EngineeringValue = null,
                State = ProcessDataState.InvalidBinding
            };
        }
        else if (sample.TimestampUtc is { } timestamp)
        {
            _lastAcceptedTimestampUtc = timestamp;
        }

        TimestampUtc = effectiveSample.TimestampUtc;
        SetState(effectiveSample.State, effectiveSample.EngineeringValue);
    }

    protected bool IsSampleForCurrentPoint(ProcessSample sample)
        => string.IsNullOrWhiteSpace(sample.PointId)
            || string.IsNullOrWhiteSpace(PointId)
            || string.Equals(sample.PointId, PointId, StringComparison.OrdinalIgnoreCase);

    protected bool IsTimestampRegression(DateTime? timestamp)
        => timestamp.HasValue
            && _lastAcceptedTimestampUtc is { } last
            && timestamp.Value < last;

    protected void SetState(ProcessDataState state, object? value)
    {
        State = state;
        StatusText = state switch
        {
            ProcessDataState.Unbound => "未绑定",
            ProcessDataState.Waiting => "未就绪",
            ProcessDataState.Good => "正常",
            ProcessDataState.Stale => "数据陈旧",
            ProcessDataState.Bad => "数据无效",
            ProcessDataState.Disconnected => "设备未连接",
            ProcessDataState.InvalidBinding => "绑定无效",
            _ => "未知"
        };
        OnPropertyChanged(nameof(PointText));
        OnPropertyChanged(nameof(SourceText));
    }

    public void ResetForRuntimeChange()
    {
        _lastAcceptedTimestampUtc = null;
        TimestampUtc = null;
        SetState(IsBound ? ProcessDataState.Waiting : ProcessDataState.Unbound, null);
    }
}

public sealed class DigitalInputPointViewModel : ProcessPointViewModel
{
    private bool? _isActive;
    private bool? _demoIsActive;

    public DigitalInputPointViewModel(ProcessSignalDefinition definition)
        : base(definition)
    {
        TrueText = definition.SignalKey == ProcessSignalCatalog.SafetyDoor ? "已闭合" : "已到位";
        FalseText = definition.SignalKey == ProcessSignalCatalog.SafetyDoor ? "未闭合" : "未到位";
    }

    public string TrueText { get; }
    public string FalseText { get; }

    public bool? IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(ActiveText));
                OnPropertyChanged(nameof(IsActiveGood));
            }
        }
    }

    public string ActiveText => State != ProcessDataState.Good || !IsActive.HasValue
        ? "未知"
        : IsActive.Value ? TrueText : FalseText;

    public bool IsActiveGood => State == ProcessDataState.Good && IsActive == true;

    public bool? DisplayIsActive => IsDemoData ? _demoIsActive : IsActive;

    public string DisplayActiveText => IsDemoData
        ? (DisplayIsActive == true ? TrueText : FalseText)
        : ActiveText;

    public bool IsDisplayedActiveGood => IsDemoData || IsActiveGood;

    public override void ApplyBinding(DevicePoint? point, string? deviceName, bool isSimulation)
    {
        IsActive = null;
        base.ApplyBinding(point, deviceName, isSimulation);
    }

    public override void ApplySample(ProcessSample sample)
    {
        if (sample.State == ProcessDataState.Good && TryConvertBool(sample.EngineeringValue, out var active))
            IsActive = active;
        else
            IsActive = null;
        base.ApplySample(sample);
        OnPropertyChanged(nameof(ActiveText));
        OnPropertyChanged(nameof(IsActiveGood));
    }

    private static bool TryConvertBool(object? value, out bool result)
    {
        if (value is bool boolean)
        {
            result = boolean;
            return true;
        }
        if (value is string text && bool.TryParse(text, out result))
            return true;
        try
        {
            if (value is not null)
            {
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture) != 0m;
                return true;
            }
        }
        catch (Exception) when (value is not null)
        {
        }
        result = false;
        return false;
    }

    protected override void OnDemoModeChanged(bool enabled)
    {
        _demoIsActive = Definition.SignalKey is ProcessSignalCatalog.SafetyDoor
            or ProcessSignalCatalog.ClampReady;
        OnPropertyChanged(nameof(DisplayIsActive));
        OnPropertyChanged(nameof(DisplayActiveText));
        OnPropertyChanged(nameof(IsDisplayedActiveGood));
    }
}

public sealed class DigitalOutputPointViewModel : ProcessPointViewModel
{
    private static readonly ICommand DemoCommand = new RelayCommand(() => { });
    private DigitalInputPointViewModel? _feedbackSource;
    private ProcessWriteState _writeState;
    private string _writeStatusText = "待操作";
    private ICommand? _openCommand;
    private ICommand? _closeCommand;

    public DigitalOutputPointViewModel(ProcessSignalDefinition definition)
        : base(definition)
    {
    }

    public DigitalInputPointViewModel? FeedbackSource
    {
        get => _feedbackSource;
        private set
        {
            if (ReferenceEquals(_feedbackSource, value))
                return;
            if (_feedbackSource is not null)
                _feedbackSource.PropertyChanged -= OnFeedbackPropertyChanged;
            _feedbackSource = value;
            if (_feedbackSource is not null)
                _feedbackSource.PropertyChanged += OnFeedbackPropertyChanged;
            OnPropertyChanged(nameof(Feedback));
            OnPropertyChanged(nameof(FeedbackText));
            OnPropertyChanged(nameof(IsFeedbackGood));
        }
    }

    public bool? Feedback => FeedbackSource?.IsActive;

    public string FeedbackText
        => FeedbackSource is { State: ProcessDataState.Good, IsActive: true } ? "已到位"
            : FeedbackSource is { State: ProcessDataState.Good, IsActive: false } ? "未到位"
            : "反馈未知";

    public bool IsFeedbackGood
        => FeedbackSource is { State: ProcessDataState.Good, IsActive: not null };

    public string DisplayFeedbackText => IsDemoData
        ? (Definition.SignalKey == ProcessSignalCatalog.InletCommand ? "已开启" : "已关闭")
        : FeedbackText;

    public bool IsDisplayedFeedbackGood => IsDemoData || IsFeedbackGood;

    public bool IsDisplayedOpen => IsDemoData
        ? Definition.SignalKey == ProcessSignalCatalog.InletCommand
        : FeedbackSource is { State: ProcessDataState.Good, IsActive: true };

    public ProcessWriteState WriteState
    {
        get => _writeState;
        private set
        {
            if (SetProperty(ref _writeState, value))
            {
                OnPropertyChanged(nameof(IsWriteInFlight));
                OnPropertyChanged(nameof(WriteStateText));
                (OpenCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
                (CloseCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string WriteStatusText
    {
        get => _writeStatusText;
        private set => SetProperty(ref _writeStatusText, value);
    }

    public string WriteStateText => WriteState switch
    {
        ProcessWriteState.Idle => "待操作",
        ProcessWriteState.Confirming => "等待确认",
        ProcessWriteState.Writing => "写入中",
        ProcessWriteState.AwaitingFeedback => "等待反馈",
        ProcessWriteState.Succeeded => WriteStatusText,
        ProcessWriteState.Failed => WriteStatusText,
        ProcessWriteState.Uncertain => "结果待核对",
        _ => "未知"
    };

    public bool IsWriteInFlight
        => WriteState is ProcessWriteState.Confirming
            or ProcessWriteState.Writing
            or ProcessWriteState.AwaitingFeedback;

    public ICommand? OpenCommand
    {
        get => _openCommand;
        private set => SetProperty(ref _openCommand, value);
    }

    public ICommand? CloseCommand
    {
        get => _closeCommand;
        private set => SetProperty(ref _closeCommand, value);
    }

    /// <summary>
    /// 演示态仅提供可点击的视觉占位命令，不进入真实输出写入链路。
    /// </summary>
    public ICommand? DisplayOpenCommand => IsDemoData ? DemoCommand : OpenCommand;

    public ICommand? DisplayCloseCommand => IsDemoData ? DemoCommand : CloseCommand;

    public void AttachFeedback(DigitalInputPointViewModel feedback)
        => FeedbackSource = feedback;

    public void AttachCommands(ICommand openCommand, ICommand closeCommand)
    {
        OpenCommand = openCommand;
        CloseCommand = closeCommand;
        OnPropertyChanged(nameof(CanOperate));
    }

    public bool CanOperate => IsBound && !IsWriteInFlight && (OpenCommand is not null || CloseCommand is not null);

    public override void ApplyBinding(DevicePoint? point, string? deviceName, bool isSimulation)
    {
        base.ApplyBinding(point, deviceName, isSimulation);
        OnPropertyChanged(nameof(CanOperate));
        (OpenCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
        (CloseCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    public void SetWriteState(ProcessWriteState state, string? message = null)
    {
        WriteState = state;
        if (!string.IsNullOrWhiteSpace(message))
            WriteStatusText = message;
        OnPropertyChanged(nameof(CanOperate));
    }

    private void OnFeedbackPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DigitalInputPointViewModel.IsActive)
            or nameof(DigitalInputPointViewModel.State)
            or nameof(DigitalInputPointViewModel.ActiveText))
        {
            OnPropertyChanged(nameof(Feedback));
            OnPropertyChanged(nameof(FeedbackText));
            OnPropertyChanged(nameof(IsFeedbackGood));
            OnPropertyChanged(nameof(DisplayFeedbackText));
            OnPropertyChanged(nameof(IsDisplayedFeedbackGood));
            OnPropertyChanged(nameof(IsDisplayedOpen));
        }
    }

    protected override void OnDemoModeChanged(bool enabled)
    {
        OnPropertyChanged(nameof(DisplayFeedbackText));
        OnPropertyChanged(nameof(IsDisplayedFeedbackGood));
        OnPropertyChanged(nameof(IsDisplayedOpen));
        OnPropertyChanged(nameof(DisplayOpenCommand));
        OnPropertyChanged(nameof(DisplayCloseCommand));
    }
}

public sealed class AnalogInputPointViewModel : ProcessPointViewModel
{
    private double? _value;
    private ProcessLimitState _limitState = ProcessLimitState.Unknown;
    private readonly ObservableCollection<ProcessTrendSample> _trendSamples = new();

    public AnalogInputPointViewModel(ProcessSignalDefinition definition)
        : base(definition)
    {
        Decimals = 3;
    }

    public double? Value
    {
        get => _value;
        private set
        {
            if (SetProperty(ref _value, value))
                OnPropertyChanged(nameof(ValueText));
        }
    }

    public int Decimals { get; }

    public string ValueText => State == ProcessDataState.Good && Value.HasValue
        ? Value.Value.ToString("F" + Decimals, CultureInfo.InvariantCulture)
        : "--";

    public string DisplayValueText => IsDemoData
        ? DemoValue.ToString("F" + Decimals, CultureInfo.InvariantCulture)
        : ValueText;

    public ProcessLimitState LimitState
    {
        get => _limitState;
        private set
        {
            if (SetProperty(ref _limitState, value))
                OnPropertyChanged(nameof(LimitText));
        }
    }

    public string LimitText => LimitState switch
    {
        ProcessLimitState.Normal => "正常",
        ProcessLimitState.Low => "偏低",
        ProcessLimitState.High => "偏高",
        ProcessLimitState.Invalid => "量程无效",
        _ => "未判定"
    };

    public string DisplayLimitText => IsDemoData ? "正常" : LimitText;

    public ObservableCollection<ProcessTrendSample> TrendSamples => _trendSamples;

    public IReadOnlyList<ProcessTrendSample> DisplayTrendSamples => IsDemoData
        ? DemoTrendSamples
        : TrendSamples;

    public override void ApplyBinding(DevicePoint? point, string? deviceName, bool isSimulation)
    {
        Value = null;
        LimitState = ProcessLimitState.Unknown;
        ClearTrend();
        base.ApplyBinding(point, deviceName, isSimulation);
    }

    public override void ApplySample(ProcessSample sample)
    {
        var isCurrentPoint = IsSampleForCurrentPoint(sample);
        var timestampRegression = IsTimestampRegression(sample.TimestampUtc);
        var value = 0d;
        var validNumeric = isCurrentPoint && !timestampRegression
            && sample.State == ProcessDataState.Good
            && TryConvertDouble(sample.EngineeringValue, out value);
        Value = validNumeric ? value : null;
        LimitState = validNumeric ? ProcessLimitState.Normal : ProcessLimitState.Unknown;
        if (validNumeric && sample.TimestampUtc.HasValue)
            AppendTrend(sample.TimestampUtc.Value, value);
        else if (timestampRegression || sample.State is ProcessDataState.Disconnected
            or ProcessDataState.Bad
            or ProcessDataState.InvalidBinding
            or ProcessDataState.Stale)
            AppendBreak();
        base.ApplySample(sample);
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(LimitText));
    }

    public void ClearTrend()
    {
        if (_trendSamples.Count == 0)
            return;
        _trendSamples.Clear();
        OnPropertyChanged(nameof(TrendSamples));
    }

    private void AppendTrend(DateTime timestampUtc, double value)
    {
        if (_trendSamples.Any(sample => sample.TimestampUtc == timestampUtc))
            return;
        _trendSamples.Add(new ProcessTrendSample(timestampUtc, value));
        while (_trendSamples.Count > 120)
            _trendSamples.RemoveAt(0);
        OnPropertyChanged(nameof(TrendSamples));
    }

    private void AppendBreak()
    {
        if (_trendSamples.LastOrDefault()?.TimestampUtc is null)
            return;
        _trendSamples.Add(new ProcessTrendSample(null, null));
        while (_trendSamples.Count > 120)
            _trendSamples.RemoveAt(0);
        OnPropertyChanged(nameof(TrendSamples));
    }

    private static bool TryConvertDouble(object? value, out double result)
    {
        try
        {
            if (value is not null)
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
        }
        catch (Exception) when (value is not null)
        {
        }
        result = 0;
        return false;
    }

    private double DemoValue => Definition.SignalKey switch
    {
        ProcessSignalCatalog.SupplyPressure => 0.800,
        ProcessSignalCatalog.MainPressure => 0.628,
        ProcessSignalCatalog.DutPressure => 0.625,
        _ => 0.000
    };

    private static readonly IReadOnlyList<ProcessTrendSample> DemoTrendSamples = new[]
    {
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(1), 0.57),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(2), 0.59),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(3), 0.56),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(4), 0.60),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(5), 0.58),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(6), 0.61),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(7), 0.60),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(8), 0.63),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(9), 0.62),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(10), 0.65),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(11), 0.63),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(12), 0.66),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(13), 0.64),
        new ProcessTrendSample(DateTime.UnixEpoch.AddSeconds(14), 0.68)
    };

    protected override void OnDemoModeChanged(bool enabled)
    {
        OnPropertyChanged(nameof(DisplayValueText));
        OnPropertyChanged(nameof(DisplayLimitText));
        OnPropertyChanged(nameof(DisplayTrendSamples));
    }
}

public sealed class AnalogOutputPointViewModel : ProcessPointViewModel
{
    private static readonly ICommand DemoApplyCommand = new RelayCommand(() => { });
    private string _targetText = string.Empty;
    private decimal? _parsedTarget;
    private bool _isDirty;
    private bool _isEditing;
    private decimal? _min;
    private decimal? _max;
    private decimal _step;
    private double? _readback;
    private ProcessDataState _readbackState = ProcessDataState.Unbound;
    private bool _readbackConfigured;
    private ProcessWriteState _writeState;
    private string _writeStatusText = "待操作";
    private ICommand? _applyCommand;

    public AnalogOutputPointViewModel(ProcessSignalDefinition definition)
        : base(definition)
    {
        _step = definition.Step ?? 0.001m;
        DecreaseTargetCommand = new RelayCommand(() => AdjustTarget(-1));
        IncreaseTargetCommand = new RelayCommand(() => AdjustTarget(1));
    }

    public string TargetText
    {
        get => IsDemoData ? "0.650" : _targetText;
        set
        {
            if (IsDemoData)
                return;
            if (SetProperty(ref _targetText, value))
            {
                IsEditing = true;
                IsDirty = true;
                ValidateTarget();
                OnPropertyChanged(nameof(TargetSliderValue));
                (ApplyCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public decimal? ParsedTarget
    {
        get => _parsedTarget;
        private set => SetProperty(ref _parsedTarget, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    public decimal? Min
    {
        get => _min;
        private set => SetProperty(ref _min, value);
    }

    public decimal? Max
    {
        get => _max;
        private set => SetProperty(ref _max, value);
    }

    public decimal Step
    {
        get => _step;
        private set => SetProperty(ref _step, value);
    }

    public bool HasReliableRange
        => Min.HasValue && Max.HasValue && Min <= Max;

    public double DisplayMinimum => IsDemoData ? 0d : (double)(Min ?? 0m);

    public double DisplayMaximum => IsDemoData ? 1d : (double)(Max ?? 1m);

    public bool IsTargetSliderEnabled => IsDemoData || HasReliableRange;

    public double TargetSliderValue
    {
        get => IsDemoData
            ? 0.650d
            : ParsedTarget.HasValue ? (double)ParsedTarget.Value : (double)(Min ?? 0m);
        set
        {
            if (IsDemoData || !HasReliableRange)
                return;
            TargetText = ((decimal)value).ToString("F3", CultureInfo.InvariantCulture);
        }
    }

    public string DisplayMinimumText => IsDemoData
        ? "0.000"
        : Min?.ToString("F3", CultureInfo.InvariantCulture) ?? "--";

    public string DisplayMaximumText => IsDemoData
        ? "1.000 MPa"
        : Max?.ToString("F3", CultureInfo.InvariantCulture) + " MPa";

    public string RangeText
        => HasReliableRange
            ? $"{Min!.Value.ToString(CultureInfo.InvariantCulture)}～{Max!.Value.ToString(CultureInfo.InvariantCulture)} MPa"
            : "未配置可靠量程";

    public double? Readback
    {
        get => _readback;
        private set
        {
            if (SetProperty(ref _readback, value))
                OnPropertyChanged(nameof(ReadbackText));
        }
    }

    public ProcessDataState ReadbackState
    {
        get => _readbackState;
        private set
        {
            if (SetProperty(ref _readbackState, value))
                OnPropertyChanged(nameof(ReadbackText));
        }
    }

    public string ReadbackText
        => IsDemoData
            ? "0.628"
            : !_readbackConfigured
            ? "未配置回读"
            : ReadbackState == ProcessDataState.Good && Readback.HasValue
                ? Readback.Value.ToString("F3", CultureInfo.InvariantCulture)
                : "--";

    public ProcessWriteState WriteState
    {
        get => _writeState;
        private set
        {
            if (SetProperty(ref _writeState, value))
            {
                OnPropertyChanged(nameof(IsWriteInFlight));
                OnPropertyChanged(nameof(WriteStateText));
                OnPropertyChanged(nameof(CanApply));
                (ApplyCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string WriteStatusText
    {
        get => _writeStatusText;
        private set => SetProperty(ref _writeStatusText, value);
    }

    public string WriteStateText => WriteState switch
    {
        ProcessWriteState.Idle => "待操作",
        ProcessWriteState.Confirming => "等待确认",
        ProcessWriteState.Writing => "写入中",
        ProcessWriteState.AwaitingFeedback => "等待回读",
        ProcessWriteState.Succeeded => WriteStatusText,
        ProcessWriteState.Failed => WriteStatusText,
        ProcessWriteState.Uncertain => "结果待核对",
        _ => "未知"
    };

    public bool IsWriteInFlight
        => WriteState is ProcessWriteState.Confirming
            or ProcessWriteState.Writing
            or ProcessWriteState.AwaitingFeedback;

    public ICommand? ApplyCommand
    {
        get => _applyCommand;
        private set => SetProperty(ref _applyCommand, value);
    }

    /// <summary>
    /// 演示态按钮只用于还原设计稿外观，不允许触发真实调压写入。
    /// </summary>
    public ICommand? DisplayApplyCommand => IsDemoData ? DemoApplyCommand : ApplyCommand;

    public bool CanApply => IsBound && HasReliableRange && IsDirty
        && ParsedTarget.HasValue && !IsWriteInFlight && ApplyCommand is not null;

    public override void ApplyBinding(DevicePoint? point, string? deviceName, bool isSimulation)
    {
        Min = null;
        Max = null;
        if (point?.EngMin is { } pointMin && point.EngMax is { } pointMax
            && pointMin <= pointMax)
        {
            Min = Math.Max(pointMin, Definition.ControlMin ?? pointMin);
            Max = Math.Min(pointMax, Definition.ControlMax ?? pointMax);
        }
        Step = Definition.Step ?? 0.001m;
        IsDirty = false;
        IsEditing = false;
        ParsedTarget = null;
        TargetText = string.Empty;
        IsDirty = false;
        IsEditing = false;
        ValidationMessage = string.Empty;
        base.ApplyBinding(point, deviceName, isSimulation);
        OnPropertyChanged(nameof(HasReliableRange));
        OnPropertyChanged(nameof(RangeText));
        OnPropertyChanged(nameof(DisplayMinimum));
        OnPropertyChanged(nameof(DisplayMaximum));
        OnPropertyChanged(nameof(IsTargetSliderEnabled));
        OnPropertyChanged(nameof(TargetSliderValue));
        OnPropertyChanged(nameof(DisplayMinimumText));
        OnPropertyChanged(nameof(DisplayMaximumText));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(DisplayApplyCommand));
    }

    public void AttachApplyCommand(ICommand command)
    {
        ApplyCommand = command;
        OnPropertyChanged(nameof(CanApply));
    }

    public ICommand DecreaseTargetCommand { get; }

    public ICommand IncreaseTargetCommand { get; }

    private void AdjustTarget(int direction)
    {
        if (IsDemoData || !HasReliableRange)
            return;
        var current = decimal.TryParse(TargetText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : Min!.Value;
        var next = Math.Clamp(current + direction * Step, Min!.Value, Max!.Value);
        TargetText = next.ToString("F3", CultureInfo.InvariantCulture);
    }

    public void ApplyReadback(ProcessSample sample)
    {
        _readbackConfigured = !string.IsNullOrWhiteSpace(sample.PointId);
        if (sample.State == ProcessDataState.Good && TryConvertDecimal(sample.EngineeringValue, out var value))
            Readback = (double)value;
        else
            Readback = null;
        ReadbackState = sample.State;
    }

    public void SetWriteState(ProcessWriteState state, string? message = null)
    {
        WriteState = state;
        if (!string.IsNullOrWhiteSpace(message))
            WriteStatusText = message;
        OnPropertyChanged(nameof(CanApply));
    }

    public bool TryGetTarget(out decimal value)
    {
        if (!ValidateTarget() || !ParsedTarget.HasValue)
        {
            value = 0m;
            return false;
        }
        value = ParsedTarget.Value;
        return true;
    }

    private bool ValidateTarget()
    {
        var text = TargetText?.Trim() ?? string.Empty;
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            ParsedTarget = null;
            ValidationMessage = string.IsNullOrWhiteSpace(text) ? "请输入调压目标" : "调压目标必须为数值";
            return false;
        }
        if (!HasReliableRange || value < Min || value > Max)
        {
            ParsedTarget = null;
            ValidationMessage = HasReliableRange ? "调压目标超出点位工程量程" : "未配置可靠量程，禁止写入";
            return false;
        }
        ParsedTarget = value;
        ValidationMessage = string.Empty;
        return true;
    }

    private string _validationMessage = string.Empty;
    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
                OnPropertyChanged(nameof(CanApply));
        }
    }

    private static bool TryConvertDecimal(object? value, out decimal result)
    {
        try
        {
            if (value is not null)
            {
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception) when (value is not null)
        {
        }
        result = 0m;
        return false;
    }

    protected override void OnDemoModeChanged(bool enabled)
    {
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(ReadbackText));
        OnPropertyChanged(nameof(DisplayMinimum));
        OnPropertyChanged(nameof(DisplayMaximum));
        OnPropertyChanged(nameof(IsTargetSliderEnabled));
        OnPropertyChanged(nameof(TargetSliderValue));
        OnPropertyChanged(nameof(DisplayMinimumText));
        OnPropertyChanged(nameof(DisplayMaximumText));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(DisplayApplyCommand));
    }
}
