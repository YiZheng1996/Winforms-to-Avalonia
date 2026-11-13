using Avalonia;
using Avalonia.Controls;
using XXX.TestBench.App.ViewModels.Process;

namespace XXX.TestBench.App.Controls.Process;

public partial class DigitalOutputControl : UserControl
{
    public static readonly StyledProperty<DigitalOutputPointViewModel?> ModelProperty =
        AvaloniaProperty.Register<DigitalOutputControl, DigitalOutputPointViewModel?>(nameof(Model));

    public DigitalOutputControl()
    {
        InitializeComponent();
    }

    public DigitalOutputPointViewModel? Model
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
