using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SolarSim.Application.Commands;
using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Equipment;

namespace SolarSim.Desktop;

public partial class MainWindow : Window
{
    private SolarProject _project = new();

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Canvas.FitToContent();
        BindProject(_project);
        _project.CreateDemoRectangularRoof();
        Canvas.FitToContent();
        RefreshChrome();
    }

    private void BindProject(SolarProject project)
    {
        if (_project is not null)
        {
            _project.ProjectChanged -= OnProjectChanged;
            _project.CalculationsUpdated -= OnProjectChanged;
            _project.History.HistoryChanged -= OnProjectChanged;
        }

        _project = project;
        _project.ProjectChanged += OnProjectChanged;
        _project.CalculationsUpdated += OnProjectChanged;
        _project.History.HistoryChanged += OnProjectChanged;
        Canvas.Project = _project;
        Canvas.FitToContent();
        RefreshChrome();
    }

    private void OnProjectChanged(string _) => RefreshChrome();

    private void OnProjectChanged() => RefreshChrome();

    private void RefreshChrome()
    {
        var calc = _project.GetCalculationSnapshot();
        var name = string.IsNullOrWhiteSpace(_project.Name) ? "Untitled" : _project.Name;
        Title = $"solarSim — {name}";
        ProjectTitle.Text = string.IsNullOrEmpty(_project.FilePath) ? name : _project.FilePath;
        StatusText.Text =
            $"{calc.TotalPanels} modules  ·  {calc.TotalPmaxWatts / 1000.0:0.##} kW DC  ·  {calc.StringCount} strings";
        Canvas.InvalidateVisual();
    }

    private void New_Click(object? sender, RoutedEventArgs e)
    {
        var project = new SolarProject();
        project.CreateDemoRectangularRoof();
        BindProject(project);
    }

    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open solarSim project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("solarSim project") { Patterns = ["*.solarproj"] },
            ],
        });
        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            BindProject(SolarProjectSerializer.LoadFromFile(path));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Unable to open project", ex.Message);
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        var path = _project.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save solarSim project",
                DefaultExtension = "solarproj",
                SuggestedFileName = string.IsNullOrWhiteSpace(_project.Name) ? "Untitled" : _project.Name,
                FileTypeChoices =
                [
                    new FilePickerFileType("solarSim project") { Patterns = ["*.solarproj"] },
                ],
            });
            path = file?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;
        }

        try
        {
            SolarProjectSerializer.SaveToFile(_project, path);
            RefreshChrome();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Unable to save project", ex.Message);
        }
    }

    private void AddPanel_Click(object? sender, RoutedEventArgs e)
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        _project.EnsureDefinition(def);
        var (x, y) = Canvas.WorldCenterMm();
        var panel = _project.AddPanelFromDefinition(def.Id, x, y);
        _project.Selection.SetSelection(componentIds: [panel.Id]);
        Canvas.SelectedPanelId = panel.Id;
        RefreshChrome();
    }

    private void Rotate_Click(object? sender, RoutedEventArgs e)
    {
        if (Canvas.SelectedPanelId is not Guid id || !_project.Graph.TryGetPanel(id, out var panel))
            return;
        _project.History.Execute(new RotatePanelCommand(_project, id, panel.RotationDegrees, panel.RotationDegrees + 90));
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (Canvas.SelectedPanelId is not Guid id) return;
        _project.History.Execute(new DeletePanelCommand(_project, id));
        Canvas.SelectedPanelId = null;
    }

    private void Undo_Click(object? sender, RoutedEventArgs e) => _project.History.Undo();

    private void Redo_Click(object? sender, RoutedEventArgs e) => _project.History.Redo();

    private void Fit_Click(object? sender, RoutedEventArgs e) => Canvas.FitToContent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var chord = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (chord && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                _project.History.Redo();
            else
                _project.History.Undo();
            e.Handled = true;
            return;
        }

        if (chord && e.Key == Key.Y)
        {
            _project.History.Redo();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            Delete_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = message,
                Margin = new Avalonia.Thickness(16),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
        };
        await dialog.ShowDialog(this);
    }
}
