using Avalonia;
using Avalonia.Controls;
using XXX.TestBench.App.ViewModels.Process;

namespace XXX.TestBench.App.Controls.Process;

/// <summary>
/// 展示模拟量输入点，并将绑定模型设为控件的数据上下文。
/// </summary>
public partial class AnalogInputControl : UserControl
{
    public static readonly StyledProperty<AnalogInputPointViewModel?> ModelProperty =
        AvaloniaProperty.Register<AnalogInputControl, AnalogInputPointViewModel?>(nameof(Model));

    public AnalogInputControl()
    {
        InitializeComponent();
    }

    public AnalogInputPointViewModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModelProperty)
            DataContext = Model;
    }
}
