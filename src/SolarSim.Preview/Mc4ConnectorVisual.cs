using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SolarSim.Domain.Electrical;

namespace SolarSim.Preview;

/// <summary>
/// Tiny MC4-style connector glyphs. Visual size stays small; callers wrap a larger hit target.
/// Not photorealistic — unmistakably male pin / female socket / mated pair.
/// </summary>
internal static class Mc4ConnectorVisual
{
    public enum Facing
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>Single port terminal (~8px visual inside a transparent layout box).</summary>
    public static FrameworkElement CreatePort(
        Polarity polarity,
        ConnectorInterface iface,
        Brush polarityBrush,
        Facing facing,
        bool occupied)
    {
        var root = new Canvas
        {
            Width = 14,
            Height = 14,
            IsHitTestVisible = false,
            Opacity = occupied ? 0.45 : 1.0,
        };

        var body = BuildHalf(iface, polarityBrush, facing, pin: true);
        Canvas.SetLeft(body, (14 - body.Width) / 2);
        Canvas.SetTop(body, (14 - body.Height) / 2);
        root.Children.Add(body);

        // Polarity ring — thin accent so +/− stay readable at a glance.
        var ring = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = polarityBrush,
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            Opacity = 0.55,
        };
        root.Children.Insert(0, ring);
        return root;
    }

    /// <summary>Mated male↔female node where two leads meet.</summary>
    public static FrameworkElement CreateMatedPair(
        Brush leftBrush,
        Brush rightBrush,
        ConnectorInterface leftIface,
        ConnectorInterface rightIface,
        bool selected)
    {
        var w = selected ? 16.0 : 14.0;
        var h = selected ? 9.0 : 8.0;
        var root = new Canvas
        {
            Width = w,
            Height = h,
            IsHitTestVisible = false,
        };

        // Prefer true genders when known; otherwise left=female socket, right=male pin.
        var leftIsFemale = leftIface switch
        {
            ConnectorInterface.Female => true,
            ConnectorInterface.Male => false,
            _ => rightIface != ConnectorInterface.Female,
        };

        var left = BuildCapsuleHalf(
            isFemale: leftIsFemale,
            brush: leftBrush,
            width: w / 2 + 1,
            height: h,
            openRight: true);
        var right = BuildCapsuleHalf(
            isFemale: !leftIsFemale,
            brush: rightBrush,
            width: w / 2 + 1,
            height: h,
            openRight: false);

        Canvas.SetLeft(left, 0);
        Canvas.SetTop(left, 0);
        Canvas.SetLeft(right, w / 2 - 1);
        Canvas.SetTop(right, 0);
        root.Children.Add(left);
        root.Children.Add(right);

        if (selected)
        {
            var outline = new Border
            {
                Width = w + 4,
                Height = h + 4,
                BorderBrush = (Brush)System.Windows.Application.Current.FindResource("AccentBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Background = Brushes.Transparent,
            };
            Canvas.SetLeft(outline, -2);
            Canvas.SetTop(outline, -2);
            root.Children.Insert(0, outline);
        }

        return root;
    }

    private static FrameworkElement BuildHalf(
        ConnectorInterface iface,
        Brush brush,
        Facing facing,
        bool pin)
    {
        var isFemale = iface switch
        {
            ConnectorInterface.Female => true,
            ConnectorInterface.Male => false,
            // Unspecified: still look like an MC4, bias by facing (outward pin).
            _ => false,
        };

        var body = new Canvas { Width = 10, Height = 10 };

        if (isFemale)
        {
            // Socket: rounded shell with dark cavity.
            var shell = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(2),
                Background = brush,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
            };
            var cavity = new Ellipse
            {
                Width = 3.5,
                Height = 3.5,
                Fill = new SolidColorBrush(Color.FromRgb(0x11, 0x12, 0x14)),
            };
            Canvas.SetLeft(shell, 1);
            Canvas.SetTop(shell, 1);
            Canvas.SetLeft(cavity, 3.25);
            Canvas.SetTop(cavity, 3.25);
            body.Children.Add(shell);
            body.Children.Add(cavity);
        }
        else
        {
            // Male: shell + protruding pin toward facing.
            var shell = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(1.5),
                Background = brush,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
            };
            var pinEl = new Border
            {
                Width = facing is Facing.Left or Facing.Right ? 3.5 : 2.2,
                Height = facing is Facing.Up or Facing.Down ? 3.5 : 2.2,
                CornerRadius = new CornerRadius(1),
                Background = Brushes.White,
            };

            switch (facing)
            {
                case Facing.Up:
                    Canvas.SetLeft(shell, 1.5);
                    Canvas.SetTop(shell, 3);
                    Canvas.SetLeft(pinEl, 4);
                    Canvas.SetTop(pinEl, 0);
                    break;
                case Facing.Down:
                    Canvas.SetLeft(shell, 1.5);
                    Canvas.SetTop(shell, 0);
                    Canvas.SetLeft(pinEl, 4);
                    Canvas.SetTop(pinEl, 6.5);
                    break;
                case Facing.Left:
                    Canvas.SetLeft(shell, 3);
                    Canvas.SetTop(shell, 1.5);
                    Canvas.SetLeft(pinEl, 0);
                    Canvas.SetTop(pinEl, 4);
                    break;
                case Facing.Right:
                    Canvas.SetLeft(shell, 0);
                    Canvas.SetTop(shell, 1.5);
                    Canvas.SetLeft(pinEl, 6.5);
                    Canvas.SetTop(pinEl, 4);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(facing), facing, null);
            }

            body.Children.Add(shell);
            if (pin) body.Children.Add(pinEl);
        }

        return body;
    }

    private static FrameworkElement BuildCapsuleHalf(
        bool isFemale,
        Brush brush,
        double width,
        double height,
        bool openRight)
    {
        var canvas = new Canvas { Width = width, Height = height };
        var radius = openRight
            ? new CornerRadius(height / 2, 1, 1, height / 2)
            : new CornerRadius(1, height / 2, height / 2, 1);

        var shell = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = radius,
            Background = brush,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
        };
        canvas.Children.Add(shell);

        if (isFemale)
        {
            var cavity = new Ellipse
            {
                Width = height * 0.35,
                Height = height * 0.35,
                Fill = new SolidColorBrush(Color.FromRgb(0x11, 0x12, 0x14)),
            };
            Canvas.SetLeft(cavity, openRight ? width - height * 0.55 : height * 0.2);
            Canvas.SetTop(cavity, (height - cavity.Height) / 2);
            canvas.Children.Add(cavity);
        }
        else
        {
            var pin = new Border
            {
                Width = 2.2,
                Height = height * 0.35,
                CornerRadius = new CornerRadius(1),
                Background = Brushes.White,
            };
            Canvas.SetLeft(pin, openRight ? width - 3.2 : 1);
            Canvas.SetTop(pin, (height - pin.Height) / 2);
            canvas.Children.Add(pin);
        }

        return canvas;
    }
}
