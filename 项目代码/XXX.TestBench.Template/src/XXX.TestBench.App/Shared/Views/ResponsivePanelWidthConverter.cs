using System.Globalization;
using Avalonia.Data.Converters;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 让主界面两侧面板在常规分辨率保持参考图宽度，在高 DPI 的较小逻辑工作区按比例收窄。
/// </summary>
public sealed class ResponsivePanelWidthConverter : IValueConverter
{
    public double Ratio { get; set; }
    public double MinWidth { get; set; }
    public double MaxWidth { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double availableWidth || availableWidth <= 0)
            return MaxWidth;

        return Math.Clamp(availableWidth * Ratio, MinWidth, MaxWidth);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
