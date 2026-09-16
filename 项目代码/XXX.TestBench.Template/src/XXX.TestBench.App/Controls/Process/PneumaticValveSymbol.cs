using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.Controls.Process;

/// <summary>
/// 简化阀符号。绿色只表示有效反馈为打开，命令状态不参与阀门颜色判定。
/// </summary>
public sealed class PneumaticValveSymbol : Control
{
    public static readonly StyledProperty<bool?> FeedbackProperty =
        AvaloniaProperty.Register<PneumaticValveSymbol, bool?>(nameof(Feedback));
    public static readonly StyledProperty<ProcessDataState> StateProperty =
        AvaloniaProperty.Register<PneumaticValveSymbol, ProcessDataState>(nameof(State));
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<PneumaticValveSymbol, string>(nameof(Label), string.Empty);

    public bool? Feedback
    {
        get => GetValue(FeedbackProperty);
        set => SetValue(FeedbackProperty, value);
    }

    public ProcessDataState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FeedbackProperty || change.Property == StateProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var stroke = State == ProcessDataState.Good && Feedback == true
            ? Color.Parse("#18A654")
            : Color.Parse("#98A2B3");
        var pen = new Pen(new SolidColorBrush(stroke), 3);
        var fill = new SolidColorBrush(Color.Parse("#FFFFFF"));
        var left = new Point(10, Bounds.Height / 2);
        var right = new Point(Math.Max(30, Bounds.Width - 10), Bounds.Height / 2);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawLine(pen, new Point(center.X - 24, center.Y - 20), new Point(center.X, center.Y));
        context.DrawLine(pen, new Point(center.X - 24, center.Y + 20), new Point(center.X, center.Y));
        context.DrawLine(pen, new Point(center.X, center.Y), new Point(center.X + 24, center.Y - 20));
        context.DrawLine(pen, new Point(center.X, center.Y), new Point(center.X + 24, center.Y + 20));
        context.DrawLine(pen, left, new Point(center.X - 24, center.Y));
        context.DrawLine(pen, new Point(center.X + 24, center.Y), right);
        context.DrawEllipse(fill, pen, center, 4, 4);
    }
}
