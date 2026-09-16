using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.Controls.Process;

/// <summary>
/// 以逻辑画布坐标绘制一段气路管线。压力颜色只来自有效压力测点状态。
/// </summary>
public sealed class PneumaticPipeControl : Control
{
    public static readonly StyledProperty<Point> StartPointProperty =
        AvaloniaProperty.Register<PneumaticPipeControl, Point>(nameof(StartPoint));
    public static readonly StyledProperty<Point> EndPointProperty =
        AvaloniaProperty.Register<PneumaticPipeControl, Point>(nameof(EndPoint));
    public static readonly StyledProperty<PipePressureState> PressureStateProperty =
        AvaloniaProperty.Register<PneumaticPipeControl, PipePressureState>(nameof(PressureState));
    public static readonly StyledProperty<bool> IsDashedProperty =
        AvaloniaProperty.Register<PneumaticPipeControl, bool>(nameof(IsDashed));

    public Point StartPoint
    {
        get => GetValue(StartPointProperty);
        set => SetValue(StartPointProperty, value);
    }

    public Point EndPoint
    {
        get => GetValue(EndPointProperty);
        set => SetValue(EndPointProperty, value);
    }

    public PipePressureState PressureState
    {
        get => GetValue(PressureStateProperty);
        set => SetValue(PressureStateProperty, value);
    }

    public bool IsDashed
    {
        get => GetValue(IsDashedProperty);
        set => SetValue(IsDashedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StartPointProperty
            || change.Property == EndPointProperty
            || change.Property == PressureStateProperty
            || change.Property == IsDashedProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var color = PressureState switch
        {
            PipePressureState.Pressurized => Color.Parse("#18A654"),
            PipePressureState.Unpressurized => Color.Parse("#98A2B3"),
            _ => Color.Parse("#C5CBD5")
        };
        var pen = new Pen(
            new SolidColorBrush(color),
            8,
            IsDashed ? DashStyle.Dash : null);
        context.DrawLine(pen, StartPoint, EndPoint);
    }
}
