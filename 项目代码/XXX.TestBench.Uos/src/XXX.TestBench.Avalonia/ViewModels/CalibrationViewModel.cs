using System.Collections.ObjectModel;
using System.Windows.Input;

namespace XXX.TestBench.Avalonia.ViewModels;

/// <summary>
/// 仅在内存中核对校准公式和 Legacy 通道清单；Zero/Gain 与 AO 应用始终保持不可执行。
/// </summary>
public sealed class CalibrationViewModel : ObservableObject
{
    private CalibrationChannelViewModel? _selectedChannel;
    private string _feedbackText = "选择通道后可在本地核对“工程值 × Gain - Zero”计算。";

    public CalibrationViewModel()
    {
        Channels = new ObservableCollection<CalibrationChannelViewModel>(CreateChannels());
        _selectedChannel = Channels.FirstOrDefault();
        // Legacy 的“预留”和“备用”是两个不同 AO 槽位，必须分别呈现；这里只展示状态，
        // 不为任一槽位创建执行命令，避免在 P3 安全合同完成前形成隐式写通道。
        Outputs = new ReadOnlyCollection<CalibrationOutputViewModel>(
        [
            new("36V 输出电流", "AO", "写入禁用"),
            new("预留", "AO", "写入禁用"),
            new("EP 阀控制", "AO", "写入禁用"),
            new("备用", "AO", "写入禁用"),
            new("160V 输出电压", "AO", "写入禁用"),
            new("36V 输出电压", "AO", "写入禁用")
        ]);
        RecalculateCommand = new RelayCommand(Recalculate, () => SelectedChannel is not null);
        ApplyCalibrationCommand = new RelayCommand(() => { }, () => false);
    }

    public ObservableCollection<CalibrationChannelViewModel> Channels { get; }
    public IReadOnlyList<CalibrationOutputViewModel> Outputs { get; }
    public ICommand RecalculateCommand { get; }
    public ICommand ApplyCalibrationCommand { get; }

    public CalibrationChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (!SetProperty(ref _selectedChannel, value))
                return;
            OnPropertyChanged(nameof(HasSelectedChannel));
            ((RelayCommand)RecalculateCommand).RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedChannel => SelectedChannel is not null;
    public bool CanApplyCalibration => false;
    public string FeedbackText => _feedbackText;
    public string SafetyBoundaryText =>
        "打开页面、选择通道和重新计算均为本地操作。Zero/Gain 应用、AO 输出和输入检测写入在管理员确认、质量/代次/回读与现场审批完成前固定禁用。";

    private void Recalculate()
    {
        if (SelectedChannel is null)
            return;
        SelectedChannel.Recalculate();
        _feedbackText = $"{SelectedChannel.Name} 本地计算结果：{SelectedChannel.CalculatedValue:0.###} {SelectedChannel.Unit}；未访问 Gateway。";
        OnPropertyChanged(nameof(FeedbackText));
    }

    private static IEnumerable<CalibrationChannelViewModel> CreateChannels()
    {
        yield return new("160V 电压", "V");
        yield return new("36V 电流", "mA");
        yield return new("36V 输出电压", "V");
        for (var index = 1; index <= 9; index++)
            yield return new($"PE{index:00}", "kPa");
    }
}

public sealed class CalibrationChannelViewModel : ObservableObject
{
    private double _engineeringValue;
    private double _zero;
    private double _gain = 1;
    private double _calculatedValue;

    public CalibrationChannelViewModel(string name, string unit)
    {
        Name = name;
        Unit = unit;
    }

    public string Name { get; }
    public string Unit { get; }
    public string QualityText => "通信未知 / 本地草稿";

    public double EngineeringValue
    {
        get => _engineeringValue;
        set => SetProperty(ref _engineeringValue, value);
    }

    public double Zero
    {
        get => _zero;
        set => SetProperty(ref _zero, value);
    }

    public double Gain
    {
        get => _gain;
        set => SetProperty(ref _gain, value);
    }

    public double CalculatedValue => _calculatedValue;

    public void Recalculate()
    {
        // 公式来自 Legacy frmHardWare.Designer；这里仅计算草稿，不写回 PLC。
        _calculatedValue = EngineeringValue * Gain - Zero;
        OnPropertyChanged(nameof(CalculatedValue));
    }
}

public sealed record CalibrationOutputViewModel(string Name, string ChannelKind, string StatusText)
{
    public bool IsEnabled => false;
}
