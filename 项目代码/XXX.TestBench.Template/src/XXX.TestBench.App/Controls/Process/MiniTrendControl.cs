using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using XXX.TestBench.App.ViewModels.Process;

namespace XXX.TestBench.App.Controls.Process;

/// <summary>
/// 轻量趋势线，只绘制当前点位模型已确认的样本；断点不会连接成假趋势。
/// </summary>
public sealed class MiniTrendControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<ProcessTrendSample>?> SamplesProperty =
        AvaloniaProperty.Register<MiniTrendControl, IReadOnlyList<ProcessTrendSample>?>(nameof(Samples));

    public IReadOnlyList<ProcessTrendSample>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SamplesProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var samples = Samples;
        if (samples is null || samples.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
            return;

        var numeric = samples.Where(sample => sample.Value.HasValue).Select(sample => sample.Value!.Value).ToArray();
        if (numeric.Length == 0)
            return;
        var min = numeric.Min();
        var max = numeric.Max();
        if (Math.Abs(max - min) < 0.0000001)
        {
            min -= 1;
            max += 1;
        }
        var pen = new Pen(new SolidColorBrush(Color.Parse("#0868F7")), 2);
        var last = default(Point?);
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            if (!sample.Value.HasValue)
            {
                last = null;
                continue;
            }
            var x = samples.Count == 1 ? 0 : Bounds.Width * index / (samples.Count - 1);
            var y = Bounds.Height - (sample.Value.Value - min) / (max - min) * Bounds.Height;
            var current = new Point(x, y);
            if (last.HasValue)
                context.DrawLine(pen, last.Value, current);
            last = current;
        }
    }
}
