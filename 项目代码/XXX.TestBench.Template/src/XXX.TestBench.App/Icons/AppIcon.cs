using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace XXX.TestBench.App.Icons;

/// <summary>
/// 应用内统一使用的细线矢量图标控件。
/// </summary>
public sealed class AppIcon : AvaloniaPath
{
    public static readonly StyledProperty<AppIconKind> KindProperty =
        AvaloniaProperty.Register<AppIcon, AppIconKind>(nameof(Kind), AppIconKind.None);

    static AppIcon()
    {
        KindProperty.Changed.AddClassHandler<AppIcon>((icon, args) =>
        {
            if (args.NewValue is AppIconKind kind)
                icon.Data = AppIconGeometry.Get(kind);
        });
    }

    public AppIcon()
    {
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Fill = Brushes.Transparent;
        Stroke = Brushes.Transparent;
        StrokeThickness = 1.6;
        StrokeLineCap = PenLineCap.Round;
        StrokeJoin = PenLineJoin.Round;
        Data = AppIconGeometry.Get(AppIconKind.None);
    }

    public AppIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
}

internal static class AppIconGeometry
{
    private static readonly Geometry Empty = Geometry.Parse("M0,0");

    // 所有图标使用 24 x 24 设计坐标，交由 Path 的 Stroke 统一渲染。
    // 这样菜单、工具栏、状态栏在不同尺寸下仍保持同一套线宽、圆角和光学重量。
    private static readonly IReadOnlyDictionary<AppIconKind, Geometry> Geometries =
        new Dictionary<AppIconKind, Geometry>
        {
            [AppIconKind.Overview] = Geometry.Parse("M4,10 L12,3 L20,10 V20 H14 V14 H10 V20 H4 Z"),
            [AppIconKind.Reports] = Geometry.Parse("M5,3 H19 V21 H5 Z M7,18 H17 M8,17 V13 M12,17 V10 M16,17 V7"),
            [AppIconKind.Parameters] = Geometry.Parse("M4,6 H20 M4,12 H20 M4,18 H20 M8,4 V8 M16,10 V14 M10,16 V20"),
            [AppIconKind.TestExecution] = Geometry.Parse("M9,3 H15 M10,3 V9 L5.2,17.4 A3,3 0 0 0 7.8,21 H16.2 A3,3 0 0 0 18.8,17.4 L14,9 V3 M7,16 H17"),
            [AppIconKind.ProcessMonitor] = Geometry.Parse("M3,4 H21 V16 H3 Z M9,20 H15 M12,16 V20 M6,13 L9,10 L11,12 L15,7 L18,10"),
            [AppIconKind.Calibration] = Geometry.Parse("M19.5,4.5 A5,5 0 0 0 13.2,9.8 L4.8,18.2 A2.1,2.1 0 1 0 7.8,21 L16.2,12.6 A5,5 0 0 0 20.5,6.4 L16.7,9.2 L14.7,6.8 Z"),
            [AppIconKind.DevicePoints] = Geometry.Parse("M4,4 H9 V9 H4 Z M15,4 H20 V9 H15 Z M4,15 H9 V20 H4 Z M15,15 H20 V20 H15 Z M9,6 H15 M6,9 V15 M18,9 V15 M9,18 H15"),
            [AppIconKind.Logs] = Geometry.Parse("M5,3 H19 V21 H5 Z M8,8 H16 M8,12 H16 M8,16 H14"),
            [AppIconKind.SystemManagement] = Geometry.Parse("M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7 M12,3 V5 M12,19 V21 M3,12 H5 M19,12 H21 M5.6,5.6 L7,7 M17,17 L18.4,18.4 M18.4,5.6 L17,7 M7,17 L5.6,18.4"),
            [AppIconKind.Logout] = Geometry.Parse("M10,17 L15,12 L10,7 M15,12 H3 M13,4 H20 V20 H13 M13,8 V4 M13,20 V16"),
            [AppIconKind.User] = Geometry.Parse("M12,4 A4,4 0 1 0 12,12 A4,4 0 1 0 12,4 M4,20 A8,7 0 0 1 20,20"),
            [AppIconKind.ChevronDown] = Geometry.Parse("M5,9 L12,16 L19,9"),
            [AppIconKind.ChevronRight] = Geometry.Parse("M9,5 L16,12 L9,19"),
            [AppIconKind.ChevronLeft] = Geometry.Parse("M15,5 L8,12 L15,19"),
            [AppIconKind.ChevronUp] = Geometry.Parse("M5,15 L12,8 L19,15"),
            [AppIconKind.Manual] = Geometry.Parse("M6,11 V7 A1.5,1.5 0 0 1 9,7 V11 V5 A1.5,1.5 0 0 1 12,5 V11 V4.5 A1.5,1.5 0 0 1 15,4.5 V11 V6 A1.5,1.5 0 0 1 18,6 V14 A6,6 0 0 1 12,20 H10 A5,5 0 0 1 5,15 V13 A2,2 0 0 1 7,13 H9"),
            [AppIconKind.Automatic] = Geometry.Parse("M7,4 H17 V7 H20 V17 H17 V20 H7 V17 H4 V7 H7 Z M9,9 H11 M13,9 H15 M9,14 H15 M12,7 V4 M12,20 V22"),
            [AppIconKind.Play] = Geometry.Parse("M8,5 L19,12 L8,19 Z"),
            [AppIconKind.Stop] = Geometry.Parse("M7,7 H17 V17 H7 Z"),
            [AppIconKind.Connection] = Geometry.Parse("M4,5 H9 V10 H4 Z M15,5 H20 V10 H15 Z M9,7 H15 M7,10 V15 H17 V10 M12,15 V20"),
            [AppIconKind.Folder] = Geometry.Parse("M3,7 H9 L11,9 H21 V20 H3 Z"),
            [AppIconKind.ShieldCheck] = Geometry.Parse("M12,3 L19,6 V11 C19,16 16,19 12,21 C8,19 5,16 5,11 V6 Z M8.5,12 L11,14.5 L16,9.5"),
            [AppIconKind.DeviceMode] = Geometry.Parse("M3,4 H21 V16 H3 Z M9,20 H15 M12,16 V20 M7,8 H17 M7,12 H12"),
            [AppIconKind.Check] = Geometry.Parse("M4,12 L9,17 L20,6"),
            [AppIconKind.Refresh] = Geometry.Parse("M19,8 A7,7 0 1 0 19,15 M19,8 H15 M19,8 V4 M5,16 A7,7 0 1 0 5,9 M5,16 H9 M5,16 V20"),
            [AppIconKind.Category] = Geometry.Parse("M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z"),
            [AppIconKind.Product] = Geometry.Parse("M4,5 H20 V19 H4 Z M12,5 V19 M4,10 H20 M7,14 A1.5,1.5 0 1 0 7,17 A1.5,1.5 0 1 0 7,14 M17,14 A1.5,1.5 0 1 0 17,17 A1.5,1.5 0 1 0 17,14"),
            [AppIconKind.TestPoint] = Geometry.Parse("M12,3 V21 M3,12 H21 M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7"),
            [AppIconKind.PointConfiguration] = Geometry.Parse("M4,8 H16 M12,4 L16,8 L12,12 M20,16 H8 M12,12 L8,16 L12,20"),
            [AppIconKind.Settings] = Geometry.Parse("M10,3 H14 L14.6,5.4 A7.5,7.5 0 0 1 16.7,6.3 L18.8,5.2 L20.8,8.7 L18.8,10.3 A7.3,7.3 0 0 1 18.8,13.7 L20.8,15.3 L18.8,18.8 L16.7,17.7 A7.5,7.5 0 0 1 14.6,18.6 L14,21 H10 L9.4,18.6 A7.5,7.5 0 0 1 7.3,17.7 L5.2,18.8 L3.2,15.3 L5.2,13.7 A7.3,7.3 0 0 1 5.2,10.3 L3.2,8.7 L5.2,5.2 L7.3,6.3 A7.5,7.5 0 0 1 9.4,5.4 Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8"),
            [AppIconKind.Add] = Geometry.Parse("M12,4 V20 M4,12 H20"),
            [AppIconKind.MoveUp] = Geometry.Parse("M12,4 V20 M5,11 L12,4 L19,11"),
            [AppIconKind.MoveDown] = Geometry.Parse("M12,4 V20 M5,13 L12,20 L19,13"),
            [AppIconKind.Import] = Geometry.Parse("M12,3 V15 M7,10 L12,15 L17,10 M4,18 H20 V21 H4 Z"),
            [AppIconKind.Export] = Geometry.Parse("M12,21 V9 M7,14 L12,9 L17,14 M4,3 H20 V6 M4,3 V21 H20"),
            [AppIconKind.Edit] = Geometry.Parse("M4,17 V21 H8 L19,10 L14,5 Z M16,7 L18,9"),
            [AppIconKind.Delete] = Geometry.Parse("M6,7 V20 H18 V7 M4,7 H20 M9,4 H15 M9,10 V17 M12,10 V17 M15,10 V17"),
            [AppIconKind.Unlock] = Geometry.Parse("M7,10 V7 A5,5 0 0 1 17,7 M5,10 H19 V21 H5 Z M12,14 V18"),
            [AppIconKind.Key] = Geometry.Parse("M8,4 A5,5 0 1 0 10.5,13 L15,17 H18 V20 H21 V17 H18 V14 H15 L11.5,10.5 A5,5 0 0 0 8,4 M8,7 A2,2 0 1 0 8,11 A2,2 0 1 0 8,7"),
            // 搜索图标用两段弧闭合镜片，再单独绘制手柄；避免单段大弧留下缺口，
            // 同时把描边端点收进 24 x 24 设计坐标，缩放到 16~18px 时不会被裁掉。
            [AppIconKind.Search] = Geometry.Parse("M10,4 A6,6 0 1 0 10,16 A6,6 0 1 0 10,4 M14.3,14.3 L19,19"),
            [AppIconKind.Reset] = Geometry.Parse("M5,5 V10 H10 M5,10 A8,8 0 1 1 8,18 H4"),
            [AppIconKind.Save] = Geometry.Parse("M5,3 H17 L20,6 V21 H5 Z M8,3 V9 H16 V5 M8,14 H16 V19 H8 Z"),
            [AppIconKind.Cancel] = Geometry.Parse("M6,6 L18,18 M18,6 L6,18"),
            [AppIconKind.SelectAll] = Geometry.Parse("M4,9 V4 H9 M15,4 H20 V9 M4,15 V20 H9 M15,20 H20 V15"),
            [AppIconKind.Warning] = Geometry.Parse("M12,4 L21,20 H3 Z M12,9 V14 M12,17 V18"),
            [AppIconKind.Flow] = Geometry.Parse("M3,12 H7 L9,7 L12,17 L15,9 L17,13 H21"),
            [AppIconKind.Signal] = Geometry.Parse("M3,12 H6 L8,8 L10,16 L13,6 L16,18 L18,12 H21"),
            [AppIconKind.CollapseUp] = Geometry.Parse("M5,15 L12,8 L19,15")
        };

    public static Geometry Get(AppIconKind kind) =>
        Geometries.TryGetValue(kind, out var geometry) ? geometry : Empty;
}
