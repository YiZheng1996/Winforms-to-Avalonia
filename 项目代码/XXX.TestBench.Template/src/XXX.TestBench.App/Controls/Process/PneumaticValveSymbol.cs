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
        var stroke = Color.Parse("#142B4E");
        var pen = new Pen(new SolidColorBrush(stroke), 2);
        var fill = new SolidColorBrush(Color.Parse("#F9FBFE"));
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var valveRect = new Rect(center.X - 30, center.Y - 25, 60, 50);
        context.DrawRectangle(fill, pen, valveRect);
        context.DrawLine(pen, new Point(center.X, center.Y - 25), new Point(center.X, center.Y + 25));
        context.DrawLine(pen, new Point(center.X - 16, center.Y + 15), new Point(center.X + 15, center.Y - 15));
        context.DrawLine(pen, new Point(center.X + 15, center.Y - 15), new Point(center.X + 7, center.Y - 13));
        context.DrawLine(pen, new Point(center.X + 15, center.Y - 15), new Point(center.X + 13, center.Y - 7));
        context.DrawLine(pen, new Point(center.X - 30, center.Y), new Point(10, center.Y));
        context.DrawLine(pen, new Point(center.X + 30, center.Y), new Point(Math.Max(30, Bounds.Width - 10), center.Y));

        // 弹簧只表达元件外形，不作为反馈状态的推断依据。
        var springStart = center.X + 30;
        var springEnd = Math.Max(springStart + 12, Bounds.Width - 10);
        var springPen = new Pen(new SolidColorBrush(stroke), 2);
        var points = new[]
        {
            new Point(springStart, center.Y),
            new Point(springStart + 7, center.Y - 9),
            new Point(springStart + 14, center.Y + 9),
            new Point(springStart + 21, center.Y - 9),
            new Point(springEnd, center.Y)
        };
        for (var index = 1; index < points.Length; index++)
            context.DrawLine(springPen, points[index - 1], points[index]);
    }
}
