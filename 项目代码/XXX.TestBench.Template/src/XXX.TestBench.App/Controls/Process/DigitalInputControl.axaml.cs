using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using XXX.TestBench.App.ViewModels.Process;

namespace XXX.TestBench.App.Controls.Process;

/// <summary>
/// 展示数字量输入点，并将绑定模型设为控件的数据上下文。
/// </summary>
public partial class DigitalInputControl : UserControl
{
    public static readonly StyledProperty<DigitalInputPointViewModel?> ModelProperty =
        AvaloniaProperty.Register<DigitalInputControl, DigitalInputPointViewModel?>(nameof(Model));

    public DigitalInputControl()
    {
        InitializeComponent();
    }

    public DigitalInputPointViewModel? Model
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
