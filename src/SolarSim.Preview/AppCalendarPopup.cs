using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SolarSim.Preview;

/// <summary>
/// Two-month popup. Click start, then end. The span is painted on both months.
/// </summary>
internal static class AppCalendarPopup
{
    public static void PickRange(UIElement target, DateOnly? start, DateOnly? end, Action<DateOnly, DateOnly> onPicked)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var leftMonth = new DateTime(start?.Year ?? today.Year, start?.Month ?? today.Month, 1);
        DateOnly? rangeStart = start;
        DateOnly? rangeEnd = end;
        DateOnly? hover = null;
        Popup? popup = null;

        var hint = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = Brush("MutedBrush"),
        };

        var leftTitle = MonthTitle();
        var rightTitle = MonthTitle();
        var leftGrid = DayGrid();
        var rightGrid = DayGrid();

        void Refresh()
        {
            var rightMonth = leftMonth.AddMonths(1);
            leftTitle.Text = leftMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            rightTitle.Text = rightMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            PaintMonth(leftGrid, DateOnly.FromDateTime(leftMonth));
            PaintMonth(rightGrid, DateOnly.FromDateTime(rightMonth));

            var previewEnd = rangeEnd ?? hover;
            if (rangeStart is DateOnly a && previewEnd is DateOnly b)
            {
                var lo = Min(a, b);
                var hi = Max(a, b);
                var days = hi.DayNumber - lo.DayNumber + 1;
                hint.Text = rangeEnd is null
                    ? $"{lo:MMM d}  →  {hi:MMM d, yyyy}  ·  {days} days  ·  click to confirm"
                    : $"{lo:MMM d, yyyy}  →  {hi:MMM d, yyyy}  ·  {days} days";
                hint.Foreground = Brush("TextBrush");
            }
            else if (rangeStart is DateOnly s)
            {
                hint.Text = $"Start {s:MMM d, yyyy} — click the end date on either month.";
                hint.Foreground = Brush("MutedBrush");
            }
            else
            {
                hint.Text = "Click the start date, then the end date. Both months stay visible.";
                hint.Foreground = Brush("MutedBrush");
            }
        }

        void PaintMonth(UniformGrid grid, DateOnly month)
        {
            grid.Children.Clear();
            foreach (var day in new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" })
            {
                grid.Children.Add(new TextBlock
                {
                    Text = day,
                    FontSize = 11,
                    Foreground = Brush("MutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }

            var first = new DateOnly(month.Year, month.Month, 1);
            var lead = (int)first.DayOfWeek;
            var days = DateTime.DaysInMonth(month.Year, month.Month);
            var cells = lead + days;
            var rows = cells <= 35 ? 35 : 42;

            for (var i = 0; i < rows; i++)
            {
                var dayNum = i - lead + 1;
                if (dayNum < 1 || dayNum > days)
                {
                    grid.Children.Add(new Border { Width = 36, Height = 32, Margin = new Thickness(1) });
                    continue;
                }

                var date = new DateOnly(month.Year, month.Month, dayNum);
                grid.Children.Add(DayCell(date));
            }
        }

        Border DayCell(DateOnly date)
        {
            var previewEnd = rangeEnd ?? hover;
            var lo = rangeStart is DateOnly a && previewEnd is DateOnly b ? Min(a, b) : (DateOnly?)null;
            var hi = rangeStart is DateOnly a2 && previewEnd is DateOnly b2 ? Max(a2, b2) : (DateOnly?)null;
            var inRange = lo is DateOnly r0 && hi is DateOnly r1 && date >= r0 && date <= r1;
            var isStart = rangeStart == date || (inRange && date == lo);
            var isEnd = rangeEnd == date || (inRange && date == hi && rangeStart != date);
            var isToday = date == today;

            Brush background = Brushes.Transparent;
            Brush foreground = Brush("TextBrush");
            var weight = FontWeights.Normal;
            if (isStart || isEnd)
            {
                background = Brush("AccentBrush");
                foreground = Brush("AccentOnBrush");
                weight = FontWeights.SemiBold;
            }
            else if (inRange)
            {
                background = Brush("AccentSoftBrush");
                foreground = Brush("TipFgBrush");
            }

            var cell = new Border
            {
                Width = 36,
                Height = 32,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(
                    isStart ? 8 : inRange ? 2 : 8,
                    isEnd ? 8 : inRange ? 2 : 8,
                    isEnd ? 8 : inRange ? 2 : 8,
                    isStart ? 8 : inRange ? 2 : 8),
                Background = background,
                BorderBrush = isToday && !isStart && !isEnd ? Brush("MutedBrush") : Brushes.Transparent,
                BorderThickness = new Thickness(isToday && !isStart && !isEnd ? 1 : 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = date.Day.ToString(CultureInfo.InvariantCulture),
                    FontSize = 12,
                    FontWeight = weight,
                    Foreground = foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (rangeStart is null || rangeEnd is not null)
                {
                    rangeStart = date;
                    rangeEnd = null;
                    hover = null;
                }
                else
                {
                    rangeEnd = date;
                    if (rangeEnd < rangeStart)
                        (rangeStart, rangeEnd) = (rangeEnd, rangeStart);
                    hover = null;
                    onPicked(rangeStart.Value, rangeEnd.Value);
                }
                Refresh();
            };
            cell.MouseEnter += (_, _) =>
            {
                if (rangeStart is not null && rangeEnd is null)
                {
                    hover = date;
                    Refresh();
                }
            };
            return cell;
        }

        Button Nav(bool forward)
        {
            var icon = new Path
            {
                Data = Geometry.Parse(forward ? "M9,5 L16,12 L9,19" : "M16,5 L9,12 L16,19"),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
            };
            var btn = new Button
            {
                Content = icon,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Style = (Style)System.Windows.Application.Current.FindResource("IconGhostButton"),
                Foreground = Brush("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            icon.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = btn });
            btn.Click += (_, _) =>
            {
                leftMonth = leftMonth.AddMonths(forward ? 1 : -1);
                Refresh();
            };
            return btn;
        }

        var leftHead = MonthHeader(leftTitle, Nav(false), Nav(true));
        var rightHead = MonthHeader(rightTitle, Nav(false), Nav(true));

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 32,
            Margin = new Thickness(0, 0, 0, 10),
        };
        header.Children.Add(leftHead);
        header.Children.Add(new Border { Width = 1, Margin = new Thickness(12, 0, 12, 0) });
        header.Children.Add(rightHead);

        var months = new StackPanel { Orientation = Orientation.Horizontal };
        months.Children.Add(MonthColumn(leftGrid));
        months.Children.Add(new Border
        {
            Width = 1,
            Background = Brush("BorderBrush"),
            Margin = new Thickness(12, 0, 12, 0),
        });
        months.Children.Add(MonthColumn(rightGrid));

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(months);
        body.Children.Add(hint);

        var shell = new Border
        {
            SnapsToDevicePixels = true,
            Background = Brush("SurfaceBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = body,
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 8,
                Opacity = 0.45,
                Color = Colors.Black,
            },
        };

        popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            Placement = PlacementMode.Bottom,
            PlacementTarget = target,
            HorizontalOffset = 0,
            VerticalOffset = 4,
            Child = shell,
        };
        Refresh();
        popup.IsOpen = true;
    }

    private static Grid MonthHeader(TextBlock title, Button? prev, Button? next)
    {
        var grid = new Grid
        {
            Width = 266,
            Height = 32,
            SnapsToDevicePixels = true,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        if (prev is not null)
        {
            Grid.SetColumn(prev, 0);
            grid.Children.Add(prev);
        }
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        if (next is not null)
        {
            Grid.SetColumn(next, 2);
            grid.Children.Add(next);
        }
        return grid;
    }

    private static TextBlock MonthTitle() => new()
    {
        FontWeight = FontWeights.SemiBold,
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        Padding = new Thickness(0, 1, 0, 0),
    };

    private static UniformGrid DayGrid() => new()
    {
        Columns = 7,
        Width = 266,
    };

    private static StackPanel MonthColumn(UniformGrid grid)
    {
        var col = new StackPanel { Width = 266 };
        col.Children.Add(grid);
        return col;
    }

    private static Brush Brush(string key) =>
        (Brush)System.Windows.Application.Current.FindResource(key);

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;
    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;
}
