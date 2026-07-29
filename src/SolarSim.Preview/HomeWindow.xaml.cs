using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SolarSim.Application.Project;
using SolarSim.Application.Serialization;

namespace SolarSim.Preview;

public partial class HomeWindow : Window
{
    private string? _chosenPath;

    public HomeWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshRecents();
    }

    private void RefreshRecents()
    {
        RecentList.Items.Clear();
        var items = RecentProjectsStore.Load();
        RecentEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in items)
        {
            var row = new ListBoxItem
            {
                Content = $"{entry.Name}\n{entry.Path}",
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
        if (dialog.ShowDialog(this) != true) return;
        _chosenPath = dialog.FileName;
        PathBox.Text = _chosenPath;
        NameBox.Text = Path.GetFileNameWithoutExtension(_chosenPath);
        HomeError.Text = "";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        HomeError.Text = "";
        if (string.IsNullOrWhiteSpace(_chosenPath))
        {
            HomeError.Text = "Browse to choose a folder and filename first.";
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_chosenPath);
            if (string.IsNullOrWhiteSpace(dir))
            {
                HomeError.Text = "That save location is invalid.";
                return;
            }

            var name = Sanitize(NameBox.Text);
            var path = Path.Combine(dir, $"{name}.solarproj");
            _chosenPath = path;
            PathBox.Text = path;

            if (File.Exists(path))
            {
                var overwrite = MessageBox.Show(this,
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
            RecentProjectsStore.Remember(path);
            OpenEditor(path);
        }
        catch (Exception ex)
        {
            HomeError.Text = ex.Message;
        }
    }

    private void OpenExisting_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "solarSim Project (*.solarproj)|*.solarproj",
            Title = "Open project from this computer",
        };
        if (dialog.ShowDialog(this) != true) return;
        OpenEditor(dialog.FileName);
    }

    private void RecentList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is ListBoxItem { Tag: string path })
            OpenEditor(path);
    }

    private void OpenEditor(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                HomeError.Text = "That file is missing on this PC.";
                RefreshRecents();
                return;
            }

            RecentProjectsStore.Remember(path);
            var editor = new MainWindow(path);
            System.Windows.Application.Current.MainWindow = editor;
            editor.Show();
            Close();
        }
        catch (Exception ex)
        {
            HomeError.Text = ex.Message;
        }
    }

    private static string Sanitize(string? raw)
    {
        raw = string.IsNullOrWhiteSpace(raw) ? "Untitled" : raw.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            raw = raw.Replace(c, '_');
        return raw;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
