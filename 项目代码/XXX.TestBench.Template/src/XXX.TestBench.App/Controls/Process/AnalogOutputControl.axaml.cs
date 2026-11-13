using Avalonia;
using Avalonia.Controls;
using XXX.TestBench.App.ViewModels.Process;

namespace XXX.TestBench.App.Controls.Process;

public partial class AnalogOutputControl : UserControl
{
    public static readonly StyledProperty<AnalogOutputPointViewModel?> ModelProperty =
        AvaloniaProperty.Register<AnalogOutputControl, AnalogOutputPointViewModel?>(nameof(Model));

    public AnalogOutputControl()
    {
        InitializeComponent();
    }

    public AnalogOutputPointViewModel? Model
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
