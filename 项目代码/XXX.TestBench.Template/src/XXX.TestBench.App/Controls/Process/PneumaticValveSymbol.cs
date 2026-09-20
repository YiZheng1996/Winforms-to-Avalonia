using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.Controls.Process;

/// <summary>
/// 方向阀符号。水平和竖直管路共用同一套双三角阀体与执行器样式，
/// 仅按管路方向旋转布局；反馈颜色只作用于执行器和阀体可见状态。
/// </summary>
public sealed class PneumaticValveSymbol : Control
{
    public static readonly StyledProperty<bool?> FeedbackProperty =
        AvaloniaProperty.Register<PneumaticValveSymbol, bool?>(nameof(Feedback));
    public static readonly StyledProperty<ProcessDataState> StateProperty =
        AvaloniaProperty.Register<PneumaticValveSymbol, ProcessDataState>(nameof(State));
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<PneumaticValveSymbol, string>(nameof(Label), string.Empty);
    public static readonly StyledProperty<bool> IsVerticalProperty =
        AvaloniaProperty.Register<PneumaticValveSymbol, bool>(nameof(IsVertical));

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

    /// <summary>
    /// 是否按竖直管路方向绘制。
    /// </summary>
    public bool IsVertical
    {
        get => GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FeedbackProperty || change.Property == StateProperty || change.Property == IsVerticalProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var hasValidFeedback = State == ProcessDataState.Good && Feedback.HasValue;
        var isOpen = hasValidFeedback && Feedback == true;
        var strokeBrush = new SolidColorBrush(Color.Parse(hasValidFeedback ? "#142B4E" : "#6D7C92"));
        var bodyBrush = new SolidColorBrush(Color.Parse(hasValidFeedback ? "#DCEBFF" : "#EEF2F7"));
        var actuatorBrush = new SolidColorBrush(Color.Parse("#0868F7"));
        var actuatorStemPen = new Pen(
            new SolidColorBrush(Color.Parse(isOpen ? "#18A654" : "#98A2B3")),
            2);
        var pen = new Pen(strokeBrush, 2);
        var pipePen = new Pen(new SolidColorBrush(Color.Parse("#0868F7")), 4);

        if (IsVertical)
            DrawVerticalValve(context, pipePen, pen, bodyBrush, actuatorBrush, actuatorStemPen);
        else
            DrawHorizontalValve(context, pipePen, pen, bodyBrush, actuatorBrush, actuatorStemPen);
    }

    private void DrawHorizontalValve(
        DrawingContext context,
        Pen pipePen,
        Pen valvePen,
        IBrush bodyBrush,
        IBrush actuatorBrush,
        Pen actuatorStemPen)
    {
        var centerY = Bounds.Height / 2;
        var centerX = Bounds.Width / 2;
        var bodyHalfWidth = Math.Min(24, Math.Max(20, (Bounds.Width - 36) / 2));
        var bodyHalfHeight = Math.Min(14, Math.Max(11, (Bounds.Height - 28) / 2));
        var bodyLeft = centerX - bodyHalfWidth;
        var bodyRight = centerX + bodyHalfWidth;
        var bodyTop = centerY - bodyHalfHeight;
        var bodyBottom = centerY + bodyHalfHeight;

        context.DrawLine(pipePen, new Point(0, centerY), new Point(Bounds.Width, centerY));
        DrawTriangle(
            context,
            valvePen,
            bodyBrush,
            new Point(bodyLeft, bodyTop),
            new Point(centerX, centerY),
            new Point(bodyLeft, bodyBottom));
        DrawTriangle(
            context,
            valvePen,
            bodyBrush,
            new Point(centerX, centerY),
            new Point(bodyRight, bodyTop),
            new Point(bodyRight, bodyBottom));

        var actuatorBottom = Math.Max(12, bodyTop - 1);
        context.DrawLine(actuatorStemPen, new Point(centerX, bodyTop), new Point(centerX, actuatorBottom));
        context.DrawRectangle(actuatorBrush, valvePen, new Rect(centerX - 7, 3, 14, 11));
    }

    private void DrawVerticalValve(
        DrawingContext context,
        Pen pipePen,
        Pen valvePen,
        IBrush bodyBrush,
        IBrush actuatorBrush,
        Pen actuatorStemPen)
    {
        var centerX = Math.Min(28, Bounds.Width / 2);
        var centerY = Bounds.Height / 2;
        const double bodyHalfWidth = 14;
        var bodyHalfHeight = Math.Min(18, Math.Max(15, (Bounds.Height - 38) / 2));
        var bodyTop = centerY - bodyHalfHeight;
        var bodyBottom = centerY + bodyHalfHeight;
        var bodyLeft = centerX - bodyHalfWidth;
        var bodyRight = centerX + bodyHalfWidth;

        context.DrawLine(pipePen, new Point(centerX, 0), new Point(centerX, Bounds.Height));
        DrawTriangle(
            context,
            valvePen,
            bodyBrush,
            new Point(bodyLeft, bodyTop),
            new Point(centerX, centerY),
            new Point(bodyRight, bodyTop));
        DrawTriangle(
            context,
            valvePen,
            bodyBrush,
            new Point(centerX, centerY),
            new Point(bodyLeft, bodyBottom),
            new Point(bodyRight, bodyBottom));

        var actuatorLeft = bodyRight + 4;
        context.DrawLine(actuatorStemPen, new Point(bodyRight, centerY), new Point(actuatorLeft, centerY));
        context.DrawRectangle(actuatorBrush, valvePen, new Rect(actuatorLeft, centerY - 7, 14, 14));
    }

    private static void DrawTriangle(
        DrawingContext context,
        Pen pen,
        IBrush fill,
        Point first,
        Point second,
        Point third)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(first, true);
            stream.LineTo(second);
            stream.LineTo(third);
            stream.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, geometry);
    }
}
