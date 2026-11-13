using Avalonia;
using Avalonia.Controls;
using XXX.TestBench.App.ViewModels.Process;

namespace XXX.TestBench.App.Controls.Process;

/// <summary>
/// 显示工艺气路图，并将图形模型设为控件的数据上下文。
/// </summary>
public partial class PneumaticDiagramControl : UserControl
{
    public static readonly StyledProperty<ProcessDiagramViewModel?> ModelProperty =
        AvaloniaProperty.Register<PneumaticDiagramControl, ProcessDiagramViewModel?>(nameof(Model));

    public PneumaticDiagramControl()
    {
        InitializeComponent();
    }

    public ProcessDiagramViewModel? Model
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
