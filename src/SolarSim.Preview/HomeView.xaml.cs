using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SolarSim.Application.Project;
using SolarSim.Application.Serialization;

namespace SolarSim.Preview;

public partial class HomeView : UserControl
{
    private string? _chosenPath;

    public event Action<string>? ProjectChosen;

    public HomeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshRecents();
            if (HomeVersionText is not null)
                HomeVersionText.Text = "Version " + AppVersion();
        };
    }

    private Window? OwnerWindow => Window.GetWindow(this);

    private void SetHomeError(string? message)
    {
        HomeError.Text = message ?? "";
        HomeError.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RefreshRecents()
    {
        RecentList.Items.Clear();
        var items = RecentProjectsStore.Load();
        RecentEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in items)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            });
            stack.Children.Add(new TextBlock
            {
                Text = entry.Path,
                FontSize = 11.5,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
            var row = new ListBoxItem
            {
                Content = stack,
                Tag = entry.Path,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 4),
            };
            RecentList.Items.Add(row);
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var name = Sanitize(NameBox.Text);
        var dialog = new SaveFileDialog
        {
            Title = "Choose where this project is born",
            Filter = "solarSim Project (*.solarproj)|*.solarproj",
            FileName = $"{name}.solarproj",
            AddExtension = true,
            DefaultExt = ".solarproj",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(OwnerWindow) != true) return;
        _chosenPath = dialog.FileName;
        PathBox.Text = _chosenPath;
        NameBox.Text = Path.GetFileNameWithoutExtension(_chosenPath);
        SetHomeError("");
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        SetHomeError("");
        if (string.IsNullOrWhiteSpace(_chosenPath))
        {
            SetHomeError("Browse to choose a folder and filename first.");
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_chosenPath);
            if (string.IsNullOrWhiteSpace(dir))
            {
                SetHomeError("That save location is invalid.");
                return;
            }

            var name = Sanitize(NameBox.Text);
            var path = Path.Combine(dir, $"{name}.solarproj");
            _chosenPath = path;
            PathBox.Text = path;

            if (File.Exists(path))
            {
                var overwrite = AppConfirmDialog.Alert(OwnerWindow,
                    $"“{name}.solarproj” already exists.\n\nOverwrite it?",
                    "solarSim",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (overwrite != MessageBoxResult.Yes) return;
            }

            var project = new SolarProject
            {
                Name = name,
                FilePath = path,
            };
            SolarProjectSerializer.SaveToFile(project, path);
            var wizard = new QuickEstimateWizardWindow(project);
            if (AppModalHost.Show(wizard) == true)
                SolarProjectSerializer.SaveToFile(project, path);
            RecentProjectsStore.Remember(path);
            OpenEditor(path);
        }
        catch (Exception ex)
        {
            SetHomeError(ex.Message);
        }
    }

    private void OpenExisting_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "solarSim Project (*.solarproj)|*.solarproj",
            Title = "Open project from this computer",
        };
        if (dialog.ShowDialog(OwnerWindow) != true) return;
        OpenEditor(dialog.FileName);
    }

    private void RecentList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is ListBoxItem { Tag: string path })
            OpenEditor(path);
    }

    private void RecentList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && RecentList.SelectedItem is ListBoxItem { Tag: string path })
            OpenEditor(path);
    }

    private void OpenEditor(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                SetHomeError("That file is missing on this PC.");
                RefreshRecents();
                return;
            }

            RecentProjectsStore.Remember(path);
            ProjectChosen?.Invoke(path);
        }
        catch (Exception ex)
        {
            SetHomeError(ex.Message);
        }
    }

    private static string Sanitize(string? raw)
    {
        raw = string.IsNullOrWhiteSpace(raw) ? "Untitled" : raw.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            raw = raw.Replace(c, '_');
        return raw;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (OwnerWindow is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => OwnerWindow?.Close();

    private static string AppVersion()
    {
        var asm = typeof(HomeView).Assembly;
        var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "1.5.13" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
