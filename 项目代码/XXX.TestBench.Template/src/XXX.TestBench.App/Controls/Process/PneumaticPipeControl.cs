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
    public static readonly StyledProperty<bool> IsControlSignalProperty =
        AvaloniaProperty.Register<PneumaticPipeControl, bool>(nameof(IsControlSignal));
    public static readonly StyledProperty<double> ArrowPositionProperty =
        AvaloniaProperty.Register<PneumaticPipeControl, double>(nameof(ArrowPosition), 0.62);

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

    public bool IsControlSignal
    {
        get => GetValue(IsControlSignalProperty);
        set => SetValue(IsControlSignalProperty, value);
    }

    /// <summary>
    /// 箭头尖端在当前管段上的相对位置，范围按 0 到 1 解释。
    /// </summary>
    public double ArrowPosition
    {
        get => GetValue(ArrowPositionProperty);
        set => SetValue(ArrowPositionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StartPointProperty
            || change.Property == EndPointProperty
            || change.Property == PressureStateProperty
            || change.Property == IsDashedProperty
            || change.Property == IsControlSignalProperty
            || change.Property == ArrowPositionProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var color = IsControlSignal
            ? Color.Parse("#18A654")
            : PressureState switch
            {
                PipePressureState.Pressurized => Color.Parse("#0868F7"),
                PipePressureState.Unpressurized => Color.Parse("#98A2B3"),
                _ => Color.Parse("#B8C4D4")
            };
        var pen = new Pen(new SolidColorBrush(color), IsControlSignal ? 2 : 4,
            IsDashed ? DashStyle.Dash : null);
        context.DrawLine(pen, StartPoint, EndPoint);

        // 主气路用小箭头标出流向；控制虚线也保留方向，但不被解释为气管。
        var direction = new Vector(EndPoint.X - StartPoint.X, EndPoint.Y - StartPoint.Y);
        var length = direction.Length;
        if (length < 18)
            return;
        var unit = direction / length;
        var normal = new Vector(-unit.Y, unit.X);
        var tip = StartPoint + direction * Math.Clamp(ArrowPosition, 0.05, 0.95);
        var basePoint = tip - unit * (IsControlSignal ? 9 : 13);
        var wing = IsControlSignal ? 5 : 7;
        context.DrawLine(pen, tip, basePoint + normal * wing);
        context.DrawLine(pen, tip, basePoint - normal * wing);
    }
}
