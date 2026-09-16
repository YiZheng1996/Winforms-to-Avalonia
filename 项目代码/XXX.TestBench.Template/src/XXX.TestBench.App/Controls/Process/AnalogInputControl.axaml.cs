using Avalonia;
using Avalonia.Controls;
using XXX.TestBench.App.ViewModels.Process;

namespace XXX.TestBench.App.Controls.Process;

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
