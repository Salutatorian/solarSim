using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SolarSim.Preview;

internal sealed class AppFlyoutItem
{
    public string Label { get; init; } = "";
    public string? Gesture { get; init; }
    public Action? Action { get; init; }
    public bool Enabled { get; init; } = true;
    public bool IsSeparator { get; init; }

    public static AppFlyoutItem Command(string label, Action action, bool enabled = true, string? gesture = null) =>
        new() { Label = label, Action = action, Enabled = enabled, Gesture = gesture };

    public static AppFlyoutItem Separator() => new() { IsSeparator = true };
}

/// <summary>
/// App-owned menu (Popup + AllowsTransparency). Avoids the white Win32 ContextMenu flash.
/// </summary>
internal static class AppFlyout
{
    private static Popup? _popup;

    public static bool IsOpen => _popup?.IsOpen == true;

    public static void Close()
    {
        if (_popup is null) return;
        _popup.IsOpen = false;
        _popup = null;
    }

    public static void ShowBelow(UIElement target, IReadOnlyList<AppFlyoutItem> items)
    {
        Open(items, popup =>
        {
            popup.Placement = PlacementMode.Bottom;
            popup.PlacementTarget = target;
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 4;
        });
    }

    public static void ShowAtMouse(IReadOnlyList<AppFlyoutItem> items)
    {
        Open(items, popup =>
        {
            popup.Placement = PlacementMode.MousePoint;
            popup.PlacementTarget = System.Windows.Application.Current.MainWindow;
            popup.HorizontalOffset = 2;
            popup.VerticalOffset = 2;
        });
    }

    private static void Open(IReadOnlyList<AppFlyoutItem> items, Action<Popup> place)
    {
        Close();
        var shell = BuildShell(items);
        var popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            Focusable = false,
            Child = shell,
        };
        place(popup);
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_popup, popup))
                _popup = null;
        };
        _popup = popup;
        popup.IsOpen = true;
        shell.Focus();
    }

    private static Border BuildShell(IReadOnlyList<AppFlyoutItem> items)
    {
        var list = new StackPanel { Margin = new Thickness(5) };
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                var rule = new Border
                {
                    Height = 1,
                    Margin = new Thickness(8, 5, 8, 5),
                    SnapsToDevicePixels = true,
                };
                rule.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
                list.Children.Add(rule);
                continue;
            }

            list.Children.Add(BuildRow(item));
        }

        var shell = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            SnapsToDevicePixels = true,
            MinWidth = 248,
            MaxWidth = 380,
            Focusable = true,
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.42,
                Color = Colors.Black,
            },
        };
        shell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        shell.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        shell.Child = list;
        shell.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        return shell;
    }

    private static Button BuildRow(AppFlyoutItem item)
    {
        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(label);
        if (!string.IsNullOrEmpty(item.Gesture))
        {
            var gesture = new TextBlock
            {
                Text = item.Gesture,
                FontSize = 11,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            gesture.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Grid.SetColumn(gesture, 1);
            grid.Children.Add(gesture);
        }

        var button = new Button
        {
            Content = grid,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(1, 1, 1, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            IsEnabled = item.Enabled,
            Focusable = false,
            BorderThickness = new Thickness(0, 0, 0, 0),
            Style = (Style)System.Windows.Application.Current.FindResource("FlyoutMenuItem"),
        };
        if (item.Action is { } action)
        {
            button.Click += (_, _) =>
            {
                Close();
                action();
            };
        }

        return button;
    }
}
