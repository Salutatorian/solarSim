using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SolarSim.Preview;

public enum AppIconKind
{
    Close,
    Minimize,
    Maximize,
    Restore,
    Undo,
    Redo,
    Sun,
    Moon,
    Settings,
    More,
    Select,
    Roof,
    Panels,
    Equipment,
    Measure,
    Suggestion,
    Layers,
    Pin,
    Rotate,
    ZoomIn,
    ZoomOut,
    Sliders,
    Plus,
    Check,
    Trash,
    Help,
    Calendar,
}

/// <summary>
/// 24×24 outline set: round caps, one weight, no unicode doodles.
/// Stroke icons inherit <see cref="Control.Foreground"/> from the parent button.
/// </summary>
public sealed class AppIcon : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(AppIconKind), typeof(AppIcon),
        new PropertyMetadata(AppIconKind.Close, OnKindChanged));

    private readonly Path _path = new()
    {
        StrokeThickness = 1.75,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Stretch = Stretch.Uniform,
        Width = 24,
        Height = 24,
    };

    public AppIcon()
    {
        Width = 16;
        Height = 16;
        Focusable = false;
        IsTabStop = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _path };
        Loaded += (_, _) => Rebuild();
    }

    public AppIconKind Kind
    {
        get => (AppIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty)
            ApplyBrush();
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((AppIcon)d).Rebuild();

    private void Rebuild()
    {
        var (data, filled) = GeometryFor(Kind);
        _path.Data = Geometry.Parse(data);
        _path.StrokeThickness = filled ? 0 : 1.75;
        ApplyBrush();
    }

    private void ApplyBrush()
    {
        var brush = Foreground ?? Brushes.White;
        var filled = Kind is AppIconKind.Select or AppIconKind.More;
        _path.Stroke = brush;
        _path.Fill = filled ? brush : Brushes.Transparent;
    }

    private static (string Data, bool Filled) GeometryFor(AppIconKind kind) => kind switch
    {
        AppIconKind.Close => ("M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5", false),
        AppIconKind.Minimize => ("M6,12 L18,12", false),
        AppIconKind.Maximize => ("M7,7 H17 V17 H7 Z", false),
        AppIconKind.Restore => ("M9,6.5 H17.5 V15 M6.5,9 H15 V17.5 H6.5 Z", false),
        AppIconKind.Undo => ("M8,10.5 H16.5 C19,10.5 20,12.5 20,15 M8,10.5 L11.8,6.8 M8,10.5 L11.8,14.2", false),
        AppIconKind.Redo => ("M16,10.5 H7.5 C5,10.5 4,12.5 4,15 M16,10.5 L12.2,6.8 M16,10.5 L12.2,14.2", false),
        AppIconKind.Sun => (
            "M12,8.4 A3.6,3.6 0 1 1 11.99,8.4 M12,3.6 V5.5 M12,18.5 V20.4 M3.6,12 H5.5 M18.5,12 H20.4 " +
            "M6.3,6.3 L7.6,7.6 M16.4,16.4 L17.7,17.7 M16.4,6.3 L17.7,5 M6.3,17.7 L7.6,16.4", false),
        AppIconKind.Moon => ("M15.8,6.2 A7.2,7.2 0 1 0 15.8,18.2 A5.2,5.2 0 0 1 15.8,6.2", false),
        AppIconKind.Settings => (
            "M10.1,4.08 L13.9,4.08 L14.72,7.28 L17.91,6.39 L19.81,9.69 L17.45,12 " +
            "L19.81,14.31 L17.91,17.61 L14.72,16.72 L13.9,19.92 L10.1,19.92 L9.27,16.72 " +
            "L6.09,17.61 L4.19,14.31 L6.55,12 L4.19,9.69 L6.09,6.39 L9.27,7.28 Z " +
            "M12,12 m-2.9,0 a2.9,2.9 0 1 1 5.8,0 a2.9,2.9 0 1 1 -5.8,0", false),
        AppIconKind.More => (
            "M4.45,12 a1.55,1.55 0 1 0 3.1,0 a1.55,1.55 0 1 0 -3.1,0 " +
            "M10.45,12 a1.55,1.55 0 1 0 3.1,0 a1.55,1.55 0 1 0 -3.1,0 " +
            "M16.45,12 a1.55,1.55 0 1 0 3.1,0 a1.55,1.55 0 1 0 -3.1,0", true),
        AppIconKind.Select => ("M6.2,3.8 L6.2,19.2 L11,14.2 L14.6,21.2 L16.8,20.2 L13.2,13.2 L19.4,13.2 Z", true),
        AppIconKind.Roof => ("M4.2,12.2 L12,5.2 L19.8,12.2 M7.5,12.2 V19.2 H16.5 V12.2", false),
        AppIconKind.Panels => ("M5.4,6.4 H18.6 V17.6 H5.4 Z M12,6.4 V17.6 M5.4,12 H18.6", false),
        AppIconKind.Equipment => (
            "M7,5.4 H17 V14.8 H7 Z M9.2,7.6 H14.8 M9.2,10 H13.4 M9.2,12.4 H14.8 M8.4,14.8 V17.2 H15.6 V14.8", false),
        AppIconKind.Measure => (
            "M5,7 V17 M19,7 V17 M5,12 H19 M7.4,12 L9.4,10 M7.4,12 L9.4,14 M16.6,12 L14.6,10 M16.6,12 L14.6,14", false),
        AppIconKind.Suggestion => (
            "M9.2,16 C7.4,14.9 6.2,12.8 6.2,10.4 A5.8,5.8 0 1 1 17.8,10.4 C17.8,12.8 16.6,14.9 14.8,16 " +
            "M9.2,16 V17.8 H14.8 V16 M9.8,19.6 H14.2", false),
        AppIconKind.Layers => (
            "M4.4,8.2 L12,4.6 L19.6,8.2 L12,11.8 Z M4.4,12 L12,15.6 L19.6,12 M4.4,15.8 L12,19.4 L19.6,15.8", false),
        AppIconKind.Pin => (
            "M12,20.4 L12,14.6 M12,4.6 A5.1,5.1 0 0 1 12,14.8 A5.1,5.1 0 0 1 12,4.6 M12,7.6 A1.7,1.7 0 1 1 11.99,7.6", false),
        AppIconKind.Rotate => ("M17.2,8.2 A6.4,6.4 0 1 1 8.4,7.6 M17.2,8.2 L17.2,4.4 M17.2,8.2 L20.8,8.2", false),
        AppIconKind.ZoomIn => (
            "M10.6,10.4 m-5.1,0 a5.1,5.1 0 1 1 10.2,0 a5.1,5.1 0 1 1 -10.2,0 M14.4,14.4 L19.4,19.4 M10.6,7.8 V13 M8,10.4 H13.2", false),
        AppIconKind.ZoomOut => (
            "M10.6,10.4 m-5.1,0 a5.1,5.1 0 1 1 10.2,0 a5.1,5.1 0 1 1 -10.2,0 M14.4,14.4 L19.4,19.4 M8,10.4 H13.2", false),
        AppIconKind.Sliders => (
            "M5,8 H19 M10.5,5.8 V10.2 M5,12 H19 M15.2,9.8 V14.2 M5,16 H19 M8.6,13.8 V18.2", false),
        AppIconKind.Plus => ("M12,6.2 V17.8 M6.2,12 H17.8", false),
        AppIconKind.Check => ("M6.2,12.2 L10.4,16.4 L18.2,8.2", false),
        AppIconKind.Trash => (
            "M8,7.2 H16 M9.6,7.2 V6.2 H14.4 V7.2 M9,9.2 V17.6 H15 V9.2 M10.6,10.6 V16 M13.4,10.6 V16", false),
        AppIconKind.Help => (
            "M12,17.6 m-0.9,0 a0.9,0.9 0 1 1 1.8,0 a0.9,0.9 0 1 1 -1.8,0 " +
            "M9.2,8.6 C9.4,6.8 10.6,5.8 12,5.8 C13.6,5.8 15,7 15,8.6 C15,10.4 12.2,10.6 12.2,13.2", false),
        AppIconKind.Calendar => (
            "M7,5.5 H17 A1.5,1.5 0 0 1 18.5,7 V18 A1.5,1.5 0 0 1 17,19.5 H7 A1.5,1.5 0 0 1 5.5,18 V7 A1.5,1.5 0 0 1 7,5.5 Z " +
            "M8,4.2 V7 M16,4.2 V7 M5.5,9.2 H18.5", false),
        _ => ("M6.5,6.5 L17.5,17.5", false),
    };
}
