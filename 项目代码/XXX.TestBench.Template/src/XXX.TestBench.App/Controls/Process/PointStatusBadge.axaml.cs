using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.Controls.Process;

public partial class PointStatusBadge : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<PointStatusBadge, string>(nameof(Text), "未绑定");
    public static readonly StyledProperty<ProcessDataState> StateProperty =
        AvaloniaProperty.Register<PointStatusBadge, ProcessDataState>(nameof(State));

    public PointStatusBadge()
    {
        InitializeComponent();
        UpdateVisual();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ProcessDataState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == StateProperty)
            UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (BadgeTextBlock is null || BadgeBorder is null)
            return;
        BadgeTextBlock.Text = Text;
        IBrush foreground;
        IBrush background;
        IBrush border;
        switch (State)
        {
            case ProcessDataState.Good:
                foreground = Brushes.DarkGreen;
                background = new SolidColorBrush(Color.Parse("#E8F7EE"));
                border = new SolidColorBrush(Color.Parse("#A8E0BC"));
                break;
            case ProcessDataState.Bad:
            case ProcessDataState.InvalidBinding:
            case ProcessDataState.Disconnected:
                foreground = Brushes.DarkRed;
                background = new SolidColorBrush(Color.Parse("#FFF0F0"));
                border = new SolidColorBrush(Color.Parse("#FFB8B8"));
                break;
            case ProcessDataState.Stale:
                foreground = Brushes.DarkOrange;
                background = new SolidColorBrush(Color.Parse("#FFF7E6"));
                border = new SolidColorBrush(Color.Parse("#FFD591"));
                break;
            default:
                foreground = new SolidColorBrush(Color.Parse("#667085"));
                background = new SolidColorBrush(Color.Parse("#F2F4F7"));
                border = new SolidColorBrush(Color.Parse("#D0D5DD"));
                break;
        }
        BadgeTextBlock.Foreground = foreground;
        BadgeBorder.Background = background;
        BadgeBorder.BorderBrush = border;
    }
}
