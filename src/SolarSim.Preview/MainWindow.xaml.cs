using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SolarSim.Application.Commands;
using SolarSim.Application.Integrations.GoogleSolar;
using SolarSim.Application.Integrations.OpenMap;
using SolarSim.Application.Integrations.Pvlib;
using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Application.Units;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;
using SolarSim.Preview.Updates;

namespace SolarSim.Preview;

public partial class MainWindow : Window
{
    private readonly SolarProject _project = new();
    private readonly Dictionary<Guid, PanelVisual> _panelVisuals = new();
    private readonly Dictionary<Guid, EquipmentVisual> _equipmentVisuals = new();
    private readonly Dictionary<Guid, WireCanvasVisual> _wireVisuals = new();

    private const double MmToPx = 0.12; // 1 mm → 0.12 px (keeps modules readable)
    /// <summary>Catch radius per axis (mm). Independent X/Y so top/bottom snaps without needing perfect column align.</summary>
    private const double SnapThresholdMm = 70;
    private const double PortHitRadiusPx = 14;
    private const double PanelGapXMm = 40;
    /// <summary>Body-to-body vertical gap (mm): clears bottom PV leads + a little air. Fixed in world space (not zoom).</summary>
    private static double PanelGapYMm => PanelPortLayoutService.LeadLengthMm + 52;

    private readonly List<UIElement> _panelPortLeadVisuals = new();

    private double _zoom = 1.0;
    private Point _panOffset = new(80, 120);
    private bool _isPanning;
    private Point _panStart;
    private Point _panOrigin;

    private readonly HashSet<Guid> _selectedPanelIds = new();
    private readonly HashSet<Guid> _selectedConnectionIds = new();
    private Guid? _selectedWaypointConnectionId;
    private int? _selectedWaypointIndex;
    private Guid? _draggingWaypointConnectionId;
    private int? _draggingWaypointIndex;
    private Guid? _draggingPanelId;
    private Guid? _rotatingEquipmentId;
    private double _rotateStartMouseAngleDeg;
    private double _rotateStartEquipmentDeg;
    private bool _rotateMoved;
    private bool _rotatingRoof;
    private bool _draggingRoofBody;
    private Point _roofDragStartCanvas;
    private double _roofDragDxMm;
    private double _roofDragDyMm;
    private Point2Mm _roofRotatePivot;
    private double _roofRotateStartMouseDeg;
    private double _roofRotateLiveDegrees;
    private readonly Dictionary<Guid, List<Point2Mm>> _roofRotateBaseline = new();
    private Border? _roofRotateHandle;

    private sealed record PanelClipboardItem(
        Guid DefinitionId,
        double OffsetXMm,
        double OffsetYMm,
        int RotationDegrees);

    private List<PanelClipboardItem>? _panelClipboard;
    private Point? _contextMenuCanvasPoint;
    private Point _dragStartMouse;
    private readonly Dictionary<Guid, (double x, double y)> _dragOrigins = new();
    private bool _dragMoved;
    private readonly List<UIElement> _alignmentGuideVisuals = new();

    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Rectangle? _marqueeRect;

    private enum WorkspacePlan
    {
        Roof,
        Interior,  // UI label: Equipment
        Combined,  // UI label: System
    }

    private enum UiTool
    {
        Select,
        Roof,
        Panel,
        Wire,
        Obstacle,
        Measure,
        Add,
        Layers,
    }

    private WorkspacePlan _workspacePlan = WorkspacePlan.Roof;
    private UiTool _uiTool = UiTool.Select;

    private enum CanvasTool
    {
        Select,
        DrawRoof,
        PlaceObstacle,
        Measure,
    }

    private CanvasTool _tool = CanvasTool.Select;
    private readonly List<UIElement> _roofVisuals = new();
    private int? _draggingRoofVertexIndex;
    private Guid? _selectedObstacleId;
    private readonly HashSet<Guid> _selectedEquipmentIds = new();

    private Guid? _wireFromPortId;
    private readonly Dictionary<Guid, FrameworkElement> _panelPortHitOverlays = new();
    private readonly List<string> _recentAddKeys = new();
    private System.Windows.Shapes.Path? _previewWire;
    private Ellipse? _hoverPortMarker;
    private TextBlock? _connectedToast;
    private TextBlock? _previewPlugHint;
    private bool _refreshRunning;
    private bool _refreshQueued;

    private Guid? _draggingWireSegmentConnectionId;
    private int _draggingWireSegmentIndex = -1; // index of segment start in full path (start+waypoints+end)
    private bool _draggingWireSegmentHorizontal;

    private enum LayersCategory
    {
        Roofs,
        Panels,
        Equipment,
    }

    private LayersCategory _layersCategory = LayersCategory.Roofs;
    private bool _suppressLayerListSelection;
    private Line? _roofRubberBandLine;
    private TextBlock? _roofLiveMeasureLabel;
    private Ellipse? _roofCloseMarker;
    private TextBlock? _roofCloseLabel;
    private TextBlock? _roofLevelBadge;
    private Line? _roofLevelGuideH;
    private Line? _roofLevelGuideV;
    private readonly List<Point2Mm> _measurePoints = new();
    private readonly List<UIElement> _measureVisuals = new();
    private Line? _measureRubberBand;
    private TextBlock? _measureLiveLabel;
    private TextBlock? _rotateDegreeLabel;
    private readonly List<UIElement> _rackingVisuals = new();
    private bool _showAttachments = false;
    private DispatcherTimer? _autoSaveTimer;
    private bool _autoSaveEnabled = true;
    private string? _lastAutoSaveError;
    private bool _applyUpdateOnCloseRequested;
    private SettingsDialog? _openSettingsDialog;
    private DispatcherTimer? _updateCheckTimer;

    public MainWindow()
    {
        InitializeComponent();
        PopulateUnitsCombo();
        PopulateClimatePresetCombo();
        PanelAppearance.Load();
        PanelAppearance.ApplyBrushes();
        PopulatePanelColorCombo();
        DesignCanvas.Focusable = true;
        // BeginInvoke avoids re-entrant rebuild crashes when mutations fire ProjectChanged mid-refresh.
        _project.ProjectChanged += _ =>
        {
            Dispatcher.BeginInvoke(RefreshAll, DispatcherPriority.Background);
            ScheduleAutoSave();
        };
        _project.CalculationsUpdated += () => Dispatcher.BeginInvoke(RefreshStatusAndInspector, DispatcherPriority.Background);
        Loaded += MainWindow_Loaded;
        ApplyWorkspacePlanUi();
        SetUiTool(UiTool.Select);
        RefreshAll();
    }

    /// <summary>Open editor with a project that already has a born path on disk.</summary>
    public MainWindow(string projectPath) : this()
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("Project path is required.", nameof(projectPath));
        LoadProjectFromPath(projectPath);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (AppVersionLabel is not null)
            AppVersionLabel.Text = GetAppVersion();
        if (ShowAttachmentsCheck is not null)
            ShowAttachmentsCheck.IsChecked = _showAttachments;
        RebuildRackingVisuals();

        if (string.IsNullOrWhiteSpace(_project.FilePath))
        {
            // Safety: editor must be opened from Home with a real path.
            MessageBox.Show(this,
                "Open or create a project from the home screen so it has a save location on this PC.",
                "solarSim",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            RecentProjectsStore.Remember(_project.FilePath);
            RefreshStatusAndInspector();
        }

        ShowWhatsNewIfPresent();
        AppUpdateService.Instance.StateChanged += OnUpdateServiceStateChanged;
        AppUpdateService.Instance.ApplyRequested += OnUpdateApplyRequested;
        Closed += (_, _) =>
        {
            AppUpdateService.Instance.StateChanged -= OnUpdateServiceStateChanged;
            AppUpdateService.Instance.ApplyRequested -= OnUpdateApplyRequested;
        };
        await StartUpdateScanningAsync();
    }

    private void ShowWhatsNewIfPresent()
    {
        var notes = AppUpdateService.ConsumeWhatsNewNotes();
        if (string.IsNullOrWhiteSpace(notes)) return;
        var (version, released, body) = AppUpdateService.ParseWhatsNewDocument(notes);
        if (string.IsNullOrWhiteSpace(version))
            version = GetAppVersion();
        var dlg = new WhatsNewDialog(version, released, body) { Owner = this };
        dlg.ShowDialog();
    }

    private async Task StartUpdateScanningAsync()
    {
        try
        {
            await AppUpdateService.Instance.CheckForUpdatesAsync(GetAppVersion());
            RefreshUpdateUi();
            // Do not auto-download — wait for Update on the toast or in Settings.
        }
        catch
        {
            // Offline / rate-limit — ignore; timer will retry.
        }

        _updateCheckTimer ??= new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        _updateCheckTimer.Tick -= UpdateCheckTimer_Tick;
        _updateCheckTimer.Tick += UpdateCheckTimer_Tick;
        _updateCheckTimer.Start();
    }

    private async void UpdateCheckTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            await AppUpdateService.Instance.CheckForUpdatesAsync(GetAppVersion());
            RefreshUpdateUi();
        }
        catch
        {
            // ignore
        }
    }

    private void OnUpdateServiceStateChanged() =>
        Dispatcher.BeginInvoke(RefreshUpdateUi);

    private void OnUpdateApplyRequested() =>
        Dispatcher.BeginInvoke(() =>
        {
            _openSettingsDialog?.Close();
            _openSettingsDialog = null;
            _applyUpdateOnCloseRequested = true;
            Close();
        });

    private void RefreshUpdateUi()
    {
        var svc = AppUpdateService.Instance;
        var hasUpdate = svc.Available is not null;
        if (SettingsUpdateBadge is not null)
            SettingsUpdateBadge.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;

        var showToast = hasUpdate && !svc.UserDismissedToast;
        if (UpdateToast is null) return;

        UpdateToast.Visibility = showToast ? Visibility.Visible : Visibility.Collapsed;
        if (!showToast || svc.Available is null) return;

        var ver = svc.Available.Version;
        if (svc.IsDownloading)
        {
            var pct = (int)Math.Round(svc.DownloadProgress01 * 100);
            UpdateToastTitle.Text = $"Downloading {ver}";
            UpdateToastBody.Text = svc.DownloadProgressIndeterminate
                ? $"Downloading… {pct}%"
                : $"{pct}% — installs automatically when finished.";
            UpdateToastCancelButton.Content = "Cancel";
            UpdateToastApplyButton.Content = "Update";
            UpdateToastApplyButton.IsEnabled = false;
        }
        else if (svc.DownloadComplete)
        {
            UpdateToastTitle.Text = $"Update {ver} ready";
            UpdateToastBody.Text = "Downloaded. Click Update to install and restart now.";
            UpdateToastCancelButton.Content = "Cancel";
            UpdateToastApplyButton.Content = "Update";
            UpdateToastApplyButton.IsEnabled = true;
        }
        else
        {
            UpdateToastTitle.Text = $"Update {ver} available";
            UpdateToastBody.Text = "Click Update to download and install. Cancel dismisses this notice.";
            UpdateToastCancelButton.Content = "Cancel";
            UpdateToastApplyButton.Content = "Update";
            UpdateToastApplyButton.IsEnabled = true;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(GetAppVersion(), RefreshUpdateUi) { Owner = this };
        _openSettingsDialog = dlg;
        dlg.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openSettingsDialog, dlg))
                _openSettingsDialog = null;
        };
        dlg.ShowDialog();
        _openSettingsDialog = null;
        RefreshUpdateUi();
    }

    private void UpdateToastLater_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateService.Instance.DismissUpdateUi();
        RefreshUpdateUi();
    }

    private void UpdateToastApply_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateService.Instance.RequestUserUpdate();
        RefreshUpdateUi();
    }

    private void LoadProjectFromPath(string path)
    {
        var loaded = SolarProjectSerializer.LoadFromFile(path);
        ReplaceProject(loaded);
    }

    private void RefreshAll()
    {
        if (_refreshRunning)
        {
            _refreshQueued = true;
            return;
        }

        _refreshRunning = true;
        try
        {
            RebuildRoofVisuals();
            RebuildPanelVisuals();
            RebuildEquipmentVisuals();
            RebuildWireVisuals();
            RebuildRackingVisuals();
            RefreshStatusAndInspector();
            RefreshLayersPanel();
            UpdateEmptyState();
            UpdateToolButtonStyles();
            UpdateSetbackBoxFromActiveRoof();
            UpdateSiteTempBoxes();
            UpdateRackingBoxes();
            UpdateLayersTabStyles();
            ApplyWorkspacePlanUi();
            UpdateLockRoofButton();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Display refresh failed:\n{ex.Message}",
                "solarSim",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _refreshRunning = false;
            if (_refreshQueued)
            {
                _refreshQueued = false;
                Dispatcher.BeginInvoke(RefreshAll, DispatcherPriority.Background);
            }
        }
    }

    private void RefreshStatusAndInspector()
    {
        try
        {
            RefreshStatusAndInspectorCore();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Status refresh error: {ex.GetType().Name}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void RefreshStatusAndInspectorCore()
    {
        PruneStaleSelection();

        var calc = _project.GetCalculationSnapshot();
        var warnings = calc.Warnings.Count(w => w.Severity != IssueSeverity.Info);
        var errors = calc.Errors.Count;

        UpdateHud(calc, errors, warnings);

        UpdateProjectNameChrome();

        StringsList.Items.Clear();
        for (var i = 0; i < calc.Strings.Count; i++)
        {
            var s = calc.Strings[i];
            var colorIndex = IndexOfStringId(s.StringId);
            var row = new ListBoxItem
            {
                Content = new StringListItem(s.StringId,
                    $"{s.DisplayName}  —  {s.PanelCount} mod  {s.TotalPmaxWatts:0.##} W  Vmp {s.VmpVolts:0.##} V"),
                Foreground = StringColorPalette.BrushForIndex(colorIndex),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Padding = new Thickness(4, 4, 4, 4),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
            };
            StringsList.Items.Add(row);
        }

        UpdateInspector();
        UpdateSelectionToolbar();
        DeleteButton.IsEnabled = _selectedPanelIds.Count > 0
            || _selectedConnectionIds.Count > 0
            || _selectedEquipmentIds.Count > 0
            || _selectedObstacleId is not null;
        if (DeleteButton.IsEnabled && InspectorPanel?.Visibility != Visibility.Visible)
            SetInspectorOpen(true);
        ZoomLabel.Text = $"{_zoom * 100:0}%";
    }

    private void UpdateHud(ProjectCalculationResult calc, int errors, int warnings)
    {
        if (HudModules is not null)
            HudModules.Text = $"{calc.TotalPanels} module{(calc.TotalPanels == 1 ? "" : "s")}";
        if (HudPower is not null)
            HudPower.Text = FormatPower(calc.TotalPmaxWatts);
        if (HudStrings is not null)
            HudStrings.Text = $"{calc.StringCount} string{(calc.StringCount == 1 ? "" : "s")}";

        if (HudHealth is not null)
        {
            if (errors > 0)
            {
                HudHealth.Text = $"✕ {errors} error{(errors == 1 ? "" : "s")}";
                HudHealth.Foreground = (Brush)FindResource("DangerBrush");
            }
            else if (warnings > 0)
            {
                HudHealth.Text = $"! {warnings} warning{(warnings == 1 ? "" : "s")}";
                HudHealth.Foreground = (Brush)FindResource("AccentBrush");
            }
            else
            {
                HudHealth.Text = "✓ No issues";
                HudHealth.Foreground = (Brush)FindResource("OkBrush");
            }
        }

        var detail = "";
        if (_selectedPanelIds.Count > 0 || _selectedConnectionIds.Count > 0)
            detail = BuildSelectionStatusText(calc, errors, warnings);
        else if (_selectedEquipmentIds.Count == 1
                 && _project.Graph.TryGetEquipment(_selectedEquipmentIds.First(), out var eq))
            detail = eq.Name;
        else if (calc.Strings.Count == 1)
        {
            var s = calc.Strings[0];
            detail = $"{s.DisplayName}  ·  {s.PanelCount} mod  ·  Vmp {s.VmpVolts:0.#} V  ·  Voc {s.VocVolts:0.#} V";
        }
        else if (calc.TotalPanels > 0)
            detail = $"~{_project.GetEnergyEstimate().EstimatedAnnualKwh:0} kWh/yr";

        if (HudDetail is not null)
            HudDetail.Text = detail;
        // Keep StatusText in sync for legacy call sites that still write to it.
        if (StatusText is not null && !string.IsNullOrEmpty(detail))
            StatusText.Text = detail;
    }

    private void PruneStaleSelection()
    {
        _selectedPanelIds.RemoveWhere(id => !_project.Graph.Panels.ContainsKey(id));
        _selectedConnectionIds.RemoveWhere(id => !_project.Graph.Connections.ContainsKey(id));
        _selectedEquipmentIds.RemoveWhere(id => !_project.Graph.Equipment.ContainsKey(id));
        if (_selectedObstacleId is Guid oid
            && (GetActiveRoofSurface() is not { } roof || roof.Obstacles.All(o => o.Id != oid)))
            _selectedObstacleId = null;
    }

    private string BuildSelectionStatusText(ProjectCalculationResult calc, int errors, int warnings)
    {
        var panelCount = _selectedPanelIds.Count;
        var wireCount = _selectedConnectionIds.Count;

        double selectedWatts = 0;
        double selectedVmp = 0;
        double selectedVoc = 0;
        double? selectedImp = null;
        double? selectedIsc = null;

        foreach (var id in _selectedPanelIds)
        {
            if (!_project.Graph.TryGetPanel(id, out var panel)) continue;
            if (!_project.Definitions.TryGetValue(panel.DefinitionId, out var def)) continue;
            selectedWatts += def.PmaxWatts;
            selectedVmp += def.VmpVolts;
            selectedVoc += def.VocVolts;
            selectedImp = selectedImp is null ? def.ImpAmps : Math.Min(selectedImp.Value, def.ImpAmps);
            selectedIsc = selectedIsc is null ? def.IscAmps : Math.Min(selectedIsc.Value, def.IscAmps);
        }

        // If selection matches one full string, show string-accurate series numbers.
        foreach (var pvString in _project.Graph.Strings)
        {
            if (pvString.PanelIdsInSeriesOrder.Count == panelCount
                && pvString.PanelIdsInSeriesOrder.All(id => _selectedPanelIds.Contains(id))
                && wireCount == 0)
            {
                var s = calc.Strings.FirstOrDefault(r => r.StringId == pvString.Id);
                if (s is null) break;
                return
                    $"SELECTION  |  {panelCount} modules  |  {FormatPower(s.TotalPmaxWatts)}  |  " +
                    $"Vmp {s.VmpVolts:0.##} V  Voc {s.VocVolts:0.##} V  " +
                    $"Imp {s.ImpAmps:0.##} A  Isc {s.IscAmps:0.##} A  |  Del to remove";
            }
        }

        var parts = new List<string> { "SELECTION" };
        if (panelCount > 0)
        {
            parts.Add($"{panelCount} panel{(panelCount == 1 ? "" : "s")}");
            parts.Add(FormatPower(selectedWatts));
            if (panelCount > 1)
                parts.Add($"ΣVmp {selectedVmp:0.##} V  ΣVoc {selectedVoc:0.##} V");
            else if (panelCount == 1)
                parts.Add($"Vmp {selectedVmp:0.##} V  Voc {selectedVoc:0.##} V  Imp {selectedImp:0.##} A  Isc {selectedIsc:0.##} A");
        }
        if (wireCount > 0)
            parts.Add($"{wireCount} wire{(wireCount == 1 ? "" : "s")}");
        parts.Add("Del to remove");
        return string.Join("  |  ", parts);
    }

    private static string FormatPower(double watts) =>
        watts >= 1000 ? $"{watts / 1000.0:0.##} kW DC" : $"{watts:0.##} W DC";


    private void ClearInspectorRows()
    {
        if (InspectorRows is not null)
            InspectorRows.Children.Clear();
        if (InspectorBody is not null)
        {
            InspectorBody.Text = "";
            InspectorBody.Visibility = Visibility.Collapsed;
        }
    }

    private void AddInspectorSection(string title)
    {
        if (InspectorRows is null) return;
        InspectorRows.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Style = (Style)FindResource("SectionLabel"),
            Margin = new Thickness(0, InspectorRows.Children.Count == 0 ? 0 : 14, 0, 8),
        });
    }

    private void AddInspectorRow(string label, string value)
    {
        if (InspectorRows is null) return;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new TextBlock { Text = label, Style = (Style)FindResource("InspectorRowLabel") };
        var right = new TextBlock { Text = value, Style = (Style)FindResource("InspectorRowValue") };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        InspectorRows.Children.Add(grid);
    }

    private void AddInspectorNote(string note)
    {
        if (InspectorRows is null) return;
        InspectorRows.Children.Add(new TextBlock
        {
            Text = note,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 12,
            LineHeight = 18,
            Margin = new Thickness(0, 0, 0, 8),
        });
    }

    private void AddInspectorBatteryDisconnectPickers(ElectricalEquipmentInstance eq)
    {
        if (InspectorRows is null) return;

        void AddCombo(string label, IEnumerable<(string Text, object Tag)> items, object current, Action<object> onPick)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("InspectorRowLabel"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var combo = new ComboBox
            {
                FontSize = 12,
                MinHeight = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            foreach (var (text, tag) in items)
            {
                var item = new ComboBoxItem { Content = text, Tag = tag };
                combo.Items.Add(item);
                if (Equals(tag, current))
                    combo.SelectedItem = item;
            }
            if (combo.SelectedItem is null && combo.Items.Count > 0)
                combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem { Tag: { } tag }) return;
                onPick(tag);
            };
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);
            InspectorRows.Children.Add(grid);
        }

        var amps = eq.RatedAmps > 0 ? eq.RatedAmps : 250;
        var series = string.IsNullOrWhiteSpace(eq.CatalogSeries) ? "DHM1B" : eq.CatalogSeries;

        AddCombo(
            "Amps",
            BatteryDisconnectGuide.AmpRatings.Select(a => ($"{a} A", (object)a)),
            amps,
            tag =>
            {
                var a = (int)tag;
                if (eq.RatedAmps == a) return;
                eq.RatedAmps = a;
                eq.Name = $"Battery Disconnect {a}A";
                UpdateInspector();
                RefreshAll();
            });

        AddCombo(
            "Series",
            BatteryDisconnectGuide.SeriesNames.Select(s => (s, (object)s)),
            series,
            tag =>
            {
                var s = (string)tag;
                if (string.Equals(eq.CatalogSeries, s, StringComparison.Ordinal)) return;
                eq.CatalogSeries = s;
                UpdateInspector();
            });

        AddInspectorRow("Rec. wire", BatteryDisconnectGuide.RecommendedMaxWire(series, amps));
        AddInspectorNote("Wire size is recommended only — you can use any gauge on the cable.");
    }

    private void AddInspectorGaugePicker(ElectricalConnection conn)
    {
        if (InspectorRows is null) return;

        var batteryCable = IsBatteryCableConnection(conn);
        var gauges = batteryCable
            ? WireGaugeFormat.BatteryCableGauges
            : WireGaugeFormat.PvStringGauges;

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = batteryCable ? "Cable" : "Gauge",
            Style = (Style)FindResource("InspectorRowLabel"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var combo = new ComboBox
        {
            FontSize = 12,
            MinHeight = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var g in gauges)
        {
            var item = new ComboBoxItem
            {
                Content = batteryCable
                    ? $"{WireGaugeFormat.ToDisplay(g)} AWG"
                    : WireGaugeFormat.ToDisplay(g),
                Tag = g,
            };
            combo.Items.Add(item);
            if (g == conn.Wire.Gauge)
                combo.SelectedItem = item;
        }

        if (combo.SelectedItem is null && combo.Items.Count > 0)
            combo.SelectedIndex = Math.Min(1, combo.Items.Count - 1); // prefer 2/0 for battery

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: WireGaugeAwg gauge }) return;
            if (conn.Wire.Gauge == gauge) return;
            conn.Wire.Gauge = gauge;
            if (batteryCable)
                conn.Wire.WireType = "Battery cable";
            UpdateInspector();
            RefreshAll();
        };

        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
        InspectorRows.Children.Add(grid);
        if (batteryCable)
        {
            var tip = TryGetBatteryDisconnectWireTip(conn);
            AddInspectorNote(tip ?? "Battery cables: 1/0–4/0 available — pick what fits; recommendations are optional.");
        }
    }

    private string? TryGetBatteryDisconnectWireTip(ElectricalConnection conn)
    {
        ElectricalEquipmentInstance? disc = null;
        if (_project.Graph.TryGetPort(conn.StartPortId, out var a)
            && _project.Graph.TryGetEquipment(a.OwnerComponentId, out var ea)
            && ea.Kind == EquipmentKind.BatteryDisconnect)
            disc = ea;
        if (disc is null
            && _project.Graph.TryGetPort(conn.EndPortId, out var b)
            && _project.Graph.TryGetEquipment(b.OwnerComponentId, out var eb)
            && eb.Kind == EquipmentKind.BatteryDisconnect)
            disc = eb;
        if (disc is null) return null;

        var series = string.IsNullOrWhiteSpace(disc.CatalogSeries) ? "DHM1B" : disc.CatalogSeries;
        var amps = disc.RatedAmps > 0 ? disc.RatedAmps : 250;
        var rec = BatteryDisconnectGuide.RecommendedMaxWire(series, amps);
        return $"Recommended for {series} @ {amps}A: {rec} (not forced — choose any size you need).";
    }

    private bool IsBatteryCableConnection(ElectricalConnection conn)
    {
        if (!_project.Graph.TryGetPort(conn.StartPortId, out var a)
            || !_project.Graph.TryGetPort(conn.EndPortId, out var b))
            return false;
        if (!_project.Graph.TryGetComponent(a.OwnerComponentId, out var oa)
            || !_project.Graph.TryGetComponent(b.OwnerComponentId, out var ob))
            return false;
        static bool IsBat(IElectricalComponent c) =>
            c is ElectricalEquipmentInstance { Kind: EquipmentKind.Battery };
        static bool IsInv(IElectricalComponent c) =>
            c is ElectricalEquipmentInstance { Kind: EquipmentKind.StringInverter };
        static bool IsDisc(IElectricalComponent c) =>
            c is ElectricalEquipmentInstance { Kind: EquipmentKind.BatteryDisconnect };
        return (IsBat(oa) && (IsInv(ob) || IsDisc(ob)))
            || (IsBat(ob) && (IsInv(oa) || IsDisc(oa)))
            || (IsDisc(oa) && IsInv(ob))
            || (IsDisc(ob) && IsInv(oa));
    }

    private void ShowInspectorDump(string heading, string body)
    {
        ClearInspectorRows();
        InspectorHeading.Text = heading;
        if (InspectorSubheading is not null)
            InspectorSubheading.Text = "";
        if (InspectorBody is not null)
        {
            InspectorBody.Text = body;
            InspectorBody.Visibility = Visibility.Visible;
        }
    }

    private static string FormatPortTooltip(ElectricalPort port)
    {
        var lines = new List<string> { port.Label, port.ConnectorFamily };
        if (port.ConnectorInterface != ConnectorInterface.Unspecified)
            lines.Add(port.ConnectorInterface.ToString());
        return string.Join("\n", lines);
    }

    private void UpdateInspector()
    {
        var hasSelection = _selectedPanelIds.Count > 0
            || _selectedConnectionIds.Count > 0
            || _selectedEquipmentIds.Count > 0
            || _selectedObstacleId is not null;

        if (SiteTempsPanel is not null)
        {
            // System tab focuses on topology; site stays in Project Settings / other plans.
            var showSite = !hasSelection
                           && _workspacePlan != WorkspacePlan.Combined;
            SiteTempsPanel.Visibility = showSite ? Visibility.Visible : Visibility.Collapsed;
        }
        if (RackingPanel is not null)
        {
            var showRacking = !hasSelection
                ? _uiTool == UiTool.Roof || _workspacePlan == WorkspacePlan.Roof
                : _selectedPanelIds.Count > 0 || _uiTool == UiTool.Roof;
            if (_workspacePlan == WorkspacePlan.Combined && !hasSelection)
                showRacking = false;
            RackingPanel.Visibility = showRacking ? Visibility.Visible : Visibility.Collapsed;
        }

        ClearInspectorRows();

        if (_selectedPanelIds.Count == 1 && _selectedConnectionIds.Count == 0
            && _project.Graph.TryGetPanel(_selectedPanelIds.First(), out var panel))
        {
            var def = _project.RequireDefinition(panel.DefinitionId);
            var coldVoc = TemperatureDeratingService.ColdVocVolts(def, _project.Site);
            var hotVmp = TemperatureDeratingService.HotVmpVolts(def, _project.Site);
            InspectorHeading.Text = "Panel";
            if (InspectorSubheading is not null)
                InspectorSubheading.Text = $"{def.Manufacturer}  ·  {def.Model}";

            AddInspectorSection("Overview");
            AddInspectorRow("Pmax", $"{def.PmaxWatts:0.##} W");

            AddInspectorSection("Transform");
            AddInspectorRow("Position",
                $"{panel.PositionXMm / 1000.0:0.##}, {panel.PositionYMm / 1000.0:0.##} m");
            AddInspectorRow("Rotation", $"{panel.RotationDegrees}°");
            AddInspectorRow("Size", $"{def.WidthMm:0.#} × {def.HeightMm:0.#} mm");

            AddInspectorSection("Electrical");
            AddInspectorRow("Vmp", $"{def.VmpVolts:0.##} V");
            AddInspectorRow("Voc", $"{def.VocVolts:0.##} V");
            AddInspectorRow("Imp", $"{def.ImpAmps:0.##} A");
            AddInspectorRow("Isc", $"{def.IscAmps:0.##} A");

            AddInspectorSection("Temperature");
            AddInspectorRow("Cold Voc", $"{coldVoc:0.##} V");
            AddInspectorRow("Hot Vmp", $"{hotVmp:0.##} V");

            AddInspectorSection("Connections");
            AddInspectorRow("PV+", panel.PositivePort.IsOccupied ? "Connected" : "Open");
            AddInspectorRow("PV−", panel.NegativePort.IsOccupied ? "Connected" : "Open");
            AddInspectorRow("Connector", def.ConnectorFamily);
            var panelString = _project.Graph.Strings.FirstOrDefault(s =>
                s.PanelIdsInSeriesOrder.Contains(panel.Id));
            if (panelString is not null)
                AddInspectorRow("String", panelString.DisplayName);
            return;
        }

        if (_selectedConnectionIds.Count == 1 && _selectedPanelIds.Count == 0
            && _project.Graph.Connections.TryGetValue(_selectedConnectionIds.First(), out var conn))
        {
            var drop = _project.CalculateWireVoltageDrop(conn.Id);
            InspectorHeading.Text = "Wire";
            if (InspectorSubheading is not null)
                InspectorSubheading.Text = $"{WireGaugeFormat.ToDisplay(conn.Wire.Gauge)}  ·  {conn.Wire.Material}";
            AddInspectorSection("Route");
            AddInspectorGaugePicker(conn);
            AddInspectorRow("One-way", _project.Units.FormatLength(conn.Wire.OneWayLengthMm));
            AddInspectorRow("Circuit", _project.Units.FormatLength(conn.Wire.OneWayLengthMm * 2));
            AddInspectorRow("Bends", $"{conn.Wire.Waypoints.Count}");
            AddInspectorRow("Type", conn.Wire.WireType.ToString());
            AddInspectorSection("Drop (est.)");
            if (drop is null)
                AddInspectorNote("Voltage drop: n/a");
            else
            {
                AddInspectorRow("ΔV", $"{drop.Value.VoltageDropVolts:0.###} V");
                AddInspectorRow("% of Vmp", $"{drop.Value.PercentDrop:0.##}%");
                AddInspectorRow("Loss", $"{drop.Value.PowerLossWatts:0.##} W");
            }
            AddInspectorNote("Design aid only — not code approval.");
            return;
        }

        if (_selectedEquipmentIds.Count == 1
            && _project.Graph.TryGetEquipment(_selectedEquipmentIds.First(), out var eq))
        {
            InspectorHeading.Text = eq.Kind.ToString();
            if (InspectorSubheading is not null)
                InspectorSubheading.Text = eq.Name;
            AddInspectorSection("Overview");
            AddInspectorRow("Ports", $"{eq.Ports.Count}");
            if (eq.Kind == EquipmentKind.PvDisconnect)
            {
                AddInspectorSection("Rating check");
                AddInspectorNote(
                    "Isolators vary — often ~1000 V DC. Current ratings include " +
                    "10 / 16 / 20 / 25 / 30 / 32 / 40 / 50 / 60 A. " +
                    "Check your panels’ Voc (cold) and Isc before picking a model.");
                AddInspectorRow("Top", "IN+ / IN− (MC4)");
                AddInspectorRow("Bottom", "OUT+ / OUT− (MC4)");
                AddInspectorNote("Design aid only — not stamped electrical approval.");
            }
            if (eq.Kind == EquipmentKind.BatteryDisconnect)
            {
                AddInspectorSection("Rating check");
                AddInspectorNote(BatteryDisconnectGuide.RatingWarning);
                AddInspectorBatteryDisconnectPickers(eq);
                AddInspectorRow("Top", "IN− / IN+");
                AddInspectorRow("Bottom", "OUT− / OUT+");
            }
            if (eq.Kind == EquipmentKind.StringInverter && eq.InverterSpecs is not null)
            {
                var report = _project.GetMpptReports().FirstOrDefault(r => r.InverterId == eq.Id);
                AddInspectorSection("MPPT");
                AddInspectorRow("Channels", $"{eq.InverterSpecs.MpptCount}");
                if (report is not null)
                {
                    var wired = report.Channels.Count(c => c.PositiveConnected || c.NegativeConnected);
                    AddInspectorRow("Wired", $"{wired}/{eq.InverterSpecs.MpptCount}");
                }
            }
            return;
        }

        if (_selectedPanelIds.Count + _selectedConnectionIds.Count + _selectedEquipmentIds.Count > 1)
        {
            InspectorHeading.Text = "Selection";
            if (InspectorSubheading is not null)
                InspectorSubheading.Text = "Multiple objects";
            AddInspectorRow("Panels", $"{_selectedPanelIds.Count}");
            AddInspectorRow("Wires", $"{_selectedConnectionIds.Count}");
            AddInspectorRow("Equipment", $"{_selectedEquipmentIds.Count}");
            return;
        }

        if (_workspacePlan == WorkspacePlan.Combined)
        {
            InspectorHeading.Text = "System";
            if (InspectorSubheading is not null)
                InspectorSubheading.Text = FriendlyProjectName();
            var sysCalc = _project.GetCalculationSnapshot();
            AddInspectorSection("Array");
            AddInspectorRow("Modules", $"{sysCalc.TotalPanels}");
            AddInspectorRow("DC power", FormatPower(sysCalc.TotalPmaxWatts));
            AddInspectorRow("Strings", $"{sysCalc.StringCount}");
            if (sysCalc.TotalPanels > 0)
                AddInspectorRow("Est. annual", $"~{_project.GetEnergyEstimate().EstimatedAnnualKwh:0} kWh");

            if (sysCalc.Strings.Count > 0)
            {
                AddInspectorSection("Strings");
                var si = 0;
                foreach (var s in sysCalc.Strings)
                {
                    AddInspectorStringRow(si, s.DisplayName,
                        $"{s.PanelCount} mod · {s.VmpVolts:0.#} Vmp · {s.VocVolts:0.#} Voc");
                    si++;
                }
            }

            AddInspectorSection("Path");
            AddInspectorNote("Modules → String → [Combiner] → [PV Disc.] → Inverter MPPT");
            AddInspectorNote("Inverter AC → [AC Disc.] → Load Center");

            var invCount = _project.Graph.Equipment.Values.Count(e => e.Kind == EquipmentKind.StringInverter);
            var combinerCount = _project.Graph.Equipment.Values.Count(e => e.Kind == EquipmentKind.CombinerBox);
            AddInspectorSection("Gear");
            AddInspectorRow("Inverters", $"{invCount}");
            AddInspectorRow("Combiners", $"{combinerCount}");
            AddInspectorNote("Design aid — not a stamped one-line. Full text: ⋯ → Single-Line.");
            return;
        }

        InspectorHeading.Text = "Project";
        if (InspectorSubheading is not null)
            InspectorSubheading.Text = FriendlyProjectName();
        var calc = _project.GetCalculationSnapshot();
        AddInspectorSection("Summary");
        AddInspectorRow("Modules", $"{calc.TotalPanels}");
        AddInspectorRow("DC power", FormatPower(calc.TotalPmaxWatts));
        AddInspectorRow("Strings", $"{calc.StringCount}");
        if (calc.TotalPanels > 0)
            AddInspectorRow("Est. annual", $"~{_project.GetEnergyEstimate().EstimatedAnnualKwh:0} kWh");
        AddInspectorNote("Site and racking settings appear below when nothing is selected.");
    }

    private void AddInspectorStringRow(int index, string name, string detail)
    {
        if (InspectorRows is null) return;
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var swatch = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(2),
            Background = StringColorPalette.BrushForIndex(index),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(swatch, 0);

        var nameBlock = new TextBlock
        {
            Text = name,
            Style = (Style)FindResource("InspectorRowLabel"),
            Foreground = StringColorPalette.BrushForIndex(index),
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetColumn(nameBlock, 1);

        var detailBlock = new TextBlock
        {
            Text = detail,
            Style = (Style)FindResource("InspectorRowValue"),
            FontSize = 11.5,
        };
        Grid.SetColumn(detailBlock, 2);

        row.Children.Add(swatch);
        row.Children.Add(nameBlock);
        row.Children.Add(detailBlock);
        InspectorRows.Children.Add(row);
    }

    private void RebuildPanelVisuals()
    {
        // Roof plan + Combined — Interior is equipment-only.
        if (!ShowsPanels)
        {
            foreach (var visual in _panelVisuals.Values.ToList())
                DesignCanvas.Children.Remove(visual.Root);
            _panelVisuals.Clear();
            return;
        }

        var existing = _panelVisuals.Keys.ToHashSet();
        var current = _project.Graph.Panels.Keys.ToHashSet();

        foreach (var removed in existing.Except(current))
        {
            DesignCanvas.Children.Remove(_panelVisuals[removed].Root);
            _panelVisuals.Remove(removed);
        }

        foreach (var panel in _project.Graph.Panels.Values)
        {
            if (!_panelVisuals.TryGetValue(panel.Id, out var visual))
            {
                visual = CreatePanelVisual(panel);
                _panelVisuals[panel.Id] = visual;
                DesignCanvas.Children.Add(visual.Root);
            }

            UpdatePanelVisual(visual, panel);
        }
    }

    private PanelVisual CreatePanelVisual(SolarPanelInstance panel)
    {
        var def = _project.RequireDefinition(panel.DefinitionId);
        var root = new Canvas { Cursor = Cursors.Arrow };

        var body = new Border
        {
            Background = GetPanelFaceBrush(),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            ClipToBounds = true,
        };

        // Sibling of Body (not clipped by panel bounds) so "270 W" stays readable when zoomed out.
        var powerLabel = new TextBlock
        {
            Text = $"{def.PmaxWatts:0} W",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        var label = new TextBlock
        {
            Text = def.DisplayName,
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedBrush"),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        var pos = CreatePortDot(true);
        var neg = CreatePortDot(false);
        var posLabel = CreatePortPolarityLabel(true);
        var negLabel = CreatePortPolarityLabel(false);

        root.Children.Add(body);
        root.Children.Add(powerLabel);
        root.Children.Add(label);
        root.Children.Add(pos);
        root.Children.Add(neg);
        root.Children.Add(posLabel);
        root.Children.Add(negLabel);

        var rotateStem = new Line
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var rotateHandle = CreateCanvaRotateHandle(
            22,
            "Rotate 90° (or press R)");
        rotateHandle.Visibility = Visibility.Collapsed;
        root.Children.Add(rotateStem);
        root.Children.Add(rotateHandle);

        var visual = new PanelVisual(
            panel.Id, root, body, powerLabel, label, pos, neg, posLabel, negLabel, rotateHandle, rotateStem);

        rotateHandle.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            // Select this panel if needed, then rotate — Canva-style affordance.
            if (!_selectedPanelIds.Contains(panel.Id))
                SetSelection(panels: new[] { panel.Id });
            var current = _project.Graph.GetPanel(panel.Id);
            _project.History.Execute(new RotatePanelCommand(
                _project, panel.Id, current.RotationDegrees, current.RotationDegrees + 90));
            e.Handled = true;
        };
        // Ports are drawn as high-Z canvas overlays (above wires). In-panel dots are position anchors only.
        pos.IsHitTestVisible = false;
        neg.IsHitTestVisible = false;
        pos.Visibility = Visibility.Collapsed;
        neg.Visibility = Visibility.Collapsed;
        root.MouseEnter += (_, _) =>
        {
            SetPortsVisible(visual, true);
            RebuildPanelPortHitOverlays();
        };
        root.MouseLeave += (_, _) =>
        {
            // Defer: moving toward a terminal used to cross empty canvas and instantly hide ports.
            Dispatcher.BeginInvoke(() =>
            {
                if (visual.Root.IsMouseOver) return;
                if (_selectedPanelIds.Contains(visual.InstanceId) || _wireFromPortId is not null)
                {
                    SetPortsVisible(visual, true);
                    RebuildPanelPortHitOverlays();
                    return;
                }
                SetPortsVisible(visual, false);
                RebuildPanelPortHitOverlays();
            }, System.Windows.Threading.DispatcherPriority.Input);
        };
        return visual;
    }

    private static ImageBrush? _panelFaceBrush;

    private static ImageBrush GetPanelFaceBrush()
    {
        if (_panelFaceBrush is not null)
            return _panelFaceBrush;

        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri("pack://application:,,,/Assets/panel-face.png", UriKind.Absolute);
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        _panelFaceBrush = new ImageBrush(bmp)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
        _panelFaceBrush.Freeze();
        return _panelFaceBrush;
    }

    /// <summary>
    /// Canva-style rotate control: white circle + clear refresh/rotate arrows (not a blob).
    /// </summary>
    private static Border CreateCanvaRotateHandle(double size, string toolTip)
    {
        var iconBox = Math.Max(12, size * 0.56);
        // Lucide-style refresh-cw — two arcs with corner arrowheads. Reads as "rotate".
        var arrows = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(
                "M21,2 L21,8 L15,8 " +
                "M3,12 A9,9 0 0 1 18.36,5.64 L21,8 " +
                "M3,22 L3,16 L9,16 " +
                "M21,12 A9,9 0 0 1 5.64,18.36 L3,16"),
            Stroke = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
            StrokeThickness = 2.0,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Stretch = Stretch.Uniform,
            Width = 24,
            Height = 24,
            IsHitTestVisible = false,
        };

        var icon = new Viewbox
        {
            Width = iconBox,
            Height = iconBox,
            Stretch = Stretch.Uniform,
            Child = arrows,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new Border
        {
            Width = size,
            Height = size,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(size / 2),
            Cursor = Cursors.Hand,
            ToolTip = toolTip,
            Child = icon,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1,
                Opacity = 0.16,
                Color = Colors.Black,
            },
        };
    }

    private Ellipse CreatePortDot(bool positive)
    {
        // Anchor-only (collapsed); real glyphs live on the overlay layer.
        var ellipse = new Ellipse
        {
            Width = PanelPortLayoutService.VisibleCircleDiameterPx,
            Height = PanelPortLayoutService.VisibleCircleDiameterPx,
            Fill = positive
                ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
                : new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)),
            Stroke = Brushes.White,
            StrokeThickness = 1.1,
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Cross,
            Tag = positive ? "PV+" : "PV-",
            ToolTip = positive ? "PV+" : "PV−",
        };
        ellipse.MouseLeftButtonDown += Port_MouseLeftButtonDown;
        return ellipse;
    }

    private static TextBlock CreatePortPolarityLabel(bool positive) => new()
    {
        Text = positive ? "+" : "−",
        FontSize = 9,
        FontWeight = FontWeights.SemiBold,
        Foreground = positive
            ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
            : new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };

    private Brush PolarityBrush(Polarity polarity) =>
        (Brush)FindResource(polarity == Polarity.Positive ? "PositiveBrush" : "NegativeBrush");

    private void UpdatePanelVisual(PanelVisual visual, SolarPanelInstance panel)
    {
        var def = _project.RequireDefinition(panel.DefinitionId);
        var size = GetPanelSizePx(def, panel.RotationDegrees);
        size = new Size(size.Width * _zoom, size.Height * _zoom);
        visual.Body.Width = size.Width;
        visual.Body.Height = size.Height;

        var (x, y) = WorldToCanvas(panel.PositionXMm, panel.PositionYMm);
        Canvas.SetLeft(visual.Root, x);
        Canvas.SetTop(visual.Root, y);
        Panel.SetZIndex(visual.Root, 100); // above PV wires (z≈40), below ports (z≈960)
        visual.Root.Width = size.Width;
        visual.Root.Height = size.Height;
        visual.Root.Background = Brushes.Transparent;

        Canvas.SetLeft(visual.Body, 0);
        Canvas.SetTop(visual.Body, 0);
        visual.Label.Visibility = Visibility.Collapsed;
        visual.PowerLabel.Visibility = Visibility.Collapsed;

        // Both terminals on the bottom service edge (side-by-side) — never top/bottom opposing.
        var layout = PanelPortLayoutService.ForAxisAlignedPanel(
            size.Width / (MmToPx * _zoom),
            size.Height / (MmToPx * _zoom));
        var s = MmToPx * _zoom;
        var d = PanelPortLayoutService.VisibleCircleDiameterPx;
        Canvas.SetLeft(visual.NegativePort, layout.NegLocalXMm * s - d / 2);
        Canvas.SetTop(visual.NegativePort, layout.NegLocalYMm * s - d / 2);
        Canvas.SetLeft(visual.PositivePort, layout.PosLocalXMm * s - d / 2);
        Canvas.SetTop(visual.PositivePort, layout.PosLocalYMm * s - d / 2);
        Canvas.SetLeft(visual.NegativeLabel, layout.NegLocalXMm * s - 3);
        Canvas.SetTop(visual.NegativeLabel, layout.NegLocalYMm * s + d / 2);
        Canvas.SetLeft(visual.PositiveLabel, layout.PosLocalXMm * s - 3);
        Canvas.SetTop(visual.PositiveLabel, layout.PosLocalYMm * s + d / 2);

        var isSelected = _selectedPanelIds.Contains(panel.Id);
        var stringIndex = StringColorPalette.IndexForPanel(_project.Graph.Strings, panel.Id);

        // Photoreal half-cut panel face. Name/watts live in the inspector only.
        visual.Body.Background = GetPanelFaceBrush();
        if (stringIndex is int si)
        {
            // Stronger string rim — easy to spot without shouting over the photo face.
            visual.Body.BorderBrush = isSelected
                ? (Brush)FindResource("AccentBrush")
                : StringColorPalette.BrushForIndex(si);
            visual.Body.BorderThickness = new Thickness(isSelected ? 2.5 : 2.25);
        }
        else
        {
            visual.Body.BorderBrush = isSelected
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush(Color.FromRgb(0xC0, 0xC8, 0xD0));
            visual.Body.BorderThickness = new Thickness(isSelected ? 2 : 1);
        }

        visual.Body.Opacity = 1.0;
        visual.Body.Effect = null;

        // Canva-style ↻ — floating below the module (no stem).
        var handleSize = Math.Clamp(22 * Math.Min(_zoom, 1.15), 20, 26);
        var gap = Math.Max(8, 10 * Math.Min(_zoom, 1.2));
        visual.RotateHandle.Width = handleSize;
        visual.RotateHandle.Height = handleSize;
        visual.RotateHandle.CornerRadius = new CornerRadius(handleSize / 2);
        Canvas.SetLeft(visual.RotateHandle, size.Width / 2 - handleSize / 2);
        Canvas.SetTop(visual.RotateHandle, size.Height + gap);
        Panel.SetZIndex(visual.RotateHandle, 20);
        visual.RotateStem.Visibility = Visibility.Collapsed;
        visual.RotateHandle.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;

        var showLabels = isSelected || _wireFromPortId is not null || visual.Root.IsMouseOver;
        SetPortsVisible(visual, showLabels);
    }

    private void RebuildWireVisuals()
    {
        foreach (var visual in _wireVisuals.Values.ToList())
            visual.RemoveFrom(DesignCanvas);
        _wireVisuals.Clear();

        var obstacles = CollectPanelObstacleRects();
        var cableBrush = NeutralCableBrush();
        var cableBrushSelected = NeutralCableBrush(selected: true);
        var laneByConnection = ComputeWireLaneIndices();
        var laneSpacing = Math.Clamp(8 * Math.Max(_zoom, 0.55), 5, 14);

        foreach (var connection in _project.Graph.Connections.Values)
        {
            if (!_project.Graph.TryGetPort(connection.StartPortId, out var start)
                || !_project.Graph.TryGetPort(connection.EndPortId, out var end))
                continue;

            var lengthP1 = GetPortWorldPoint(start);
            var lengthP2 = GetPortWorldPoint(end);
            connection.Wire.OneWayLengthMm = WireRouting.LengthMm(
                lengthP1, connection.Wire.Waypoints, lengthP2);

            if (!ConnectionVisibleInCurrentPlan(connection))
                continue;

            var p1 = GetPortCanvasPoint(start);
            var p2 = GetPortCanvasPoint(end);
            var selected = _selectedConnectionIds.Contains(connection.Id);
            var thickness = selected ? 2.5 : 1.75;
            var stroke = selected ? cableBrushSelected : cableBrush;

            // Wires sit under panels normally; selected wire comes forward for inspection.
            var wireZ = selected ? 720 : 40;
            var hitZ = selected ? 721 : 41;

            laneByConnection.TryGetValue(connection.Id, out var lane);
            var route = BuildPvWireRoute(start, end, p1, p2, connection, obstacles, lane.offset * laneSpacing);
            var pathPoints = route.PathPoints.Select(v => new Point(v.X, v.Y)).ToList();

            var visual = new WireCanvasVisual { ConnectionId = connection.Id, HitPoints = pathPoints };

            // Fat invisible hit geometry (clickable without thick visible cable).
            var hitPath = CreateWirePath(route, Brushes.Transparent, 14, connection.Id, isHitTarget: true, rounded: false);
            visual.Shapes.Add(hitPath);
            AddWireShape(hitPath, z: hitZ);

            // Visible Smart Wiring cable (rounded ortho elbows).
            var cablePath = CreateWirePath(route, stroke, thickness, connection.Id, isHitTarget: false, rounded: true);
            visual.Shapes.Add(cablePath);
            AddWireShape(cablePath, z: wireZ);

            // Short polarity tips at each terminal (not half-and-half cable coloring).
            var tipLen = Math.Clamp(10 * _zoom, 8, 14);
            var n1 = PortExitNormalCanvas(start);
            var n2 = PortExitNormalCanvas(end);
            var tipA = CreatePolarityTip(p1, n1, tipLen, PolarityBrush(start.Polarity), connection.Id);
            var tipB = CreatePolarityTip(p2, n2, tipLen, PolarityBrush(end.Polarity), connection.Id);
            visual.Shapes.Add(tipA);
            visual.Shapes.Add(tipB);
            AddWireShape(tipA, z: wireZ + 1);
            AddWireShape(tipB, z: wireZ + 1);

            // No mid-wire MC4 / connector node — the cable alone is the connection.

            if (selected)
            {
                for (var i = 0; i < connection.Wire.Waypoints.Count; i++)
                {
                    var wp = connection.Wire.Waypoints[i];
                    var (cx, cy) = WorldToCanvas(wp.X, wp.Y);
                    var handleSelected = _selectedWaypointConnectionId == connection.Id
                                         && _selectedWaypointIndex == i;
                    var size = handleSelected ? 12.0 : 9.0;
                    var handle = new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = handleSelected
                            ? (Brush)FindResource("AccentBrush")
                            : Brushes.White,
                        Stroke = (Brush)FindResource("AccentBrush"),
                        StrokeThickness = 2,
                        Tag = (connection.Id, i),
                        Cursor = Cursors.SizeAll,
                    };
                    Canvas.SetLeft(handle, cx - size / 2);
                    Canvas.SetTop(handle, cy - size / 2);
                    Panel.SetZIndex(handle, 900);
                    var index = i;
                    handle.MouseLeftButtonDown += (_, e) =>
                    {
                        _selectedWaypointConnectionId = connection.Id;
                        _selectedWaypointIndex = index;
                        _draggingWaypointConnectionId = connection.Id;
                        _draggingWaypointIndex = index;
                        if (!_selectedConnectionIds.Contains(connection.Id))
                            SelectConnection(connection.Id);
                        DesignCanvas.CaptureMouse();
                        e.Handled = true;
                        RefreshStatusAndInspector();
                        RebuildWireVisuals();
                    };
                    visual.Handles.Add(handle);
                    DesignCanvas.Children.Add(handle);
                }
            }

            _wireVisuals[connection.Id] = visual;
        }

        RebuildPanelPortHitOverlays();
    }

    /// <summary>
    /// Stable parallel-lane index within each owner-pair bundle (centered around 0).
    /// </summary>
    private Dictionary<Guid, (int index, double offset)> ComputeWireLaneIndices()
    {
        var groups = new Dictionary<(Guid, Guid), List<Guid>>();
        foreach (var connection in _project.Graph.Connections.Values)
        {
            if (!_project.Graph.TryGetPort(connection.StartPortId, out var start)
                || !_project.Graph.TryGetPort(connection.EndPortId, out var end))
                continue;
            if (connection.Wire.Waypoints.Count > 0)
                continue; // manual routes keep absolute geometry

            var a = start.OwnerComponentId;
            var b = end.OwnerComponentId;
            var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<Guid>();
                groups[key] = list;
            }

            list.Add(connection.Id);
        }

        var result = new Dictionary<Guid, (int index, double offset)>();
        foreach (var list in groups.Values)
        {
            list.Sort();
            var n = list.Count;
            for (var i = 0; i < n; i++)
            {
                var offset = n <= 1 ? 0 : i - (n - 1) / 2.0;
                result[list[i]] = (i, offset);
            }
        }

        return result;
    }

    private PvWireRouteResult BuildPvWireRoute(
        ElectricalPort start,
        ElectricalPort end,
        Point p1,
        Point p2,
        ElectricalConnection connection,
        IReadOnlyList<PvRect> obstacles,
        double laneOffset = 0)
    {
        PvRect? startPanel = null;
        PvRect? endPanel = null;
        if (_panelVisuals.TryGetValue(start.OwnerComponentId, out var v1))
            startPanel = ToPvRect(PanelCanvasRect(v1, pad: 4));
        if (_panelVisuals.TryGetValue(end.OwnerComponentId, out var v2))
            endPanel = ToPvRect(PanelCanvasRect(v2, pad: 4));

        IReadOnlyList<PvVec2>? manual = null;
        if (connection.Wire.Waypoints.Count > 0)
        {
            manual = connection.Wire.Waypoints
                .Select(wp =>
                {
                    var (cx, cy) = WorldToCanvas(wp.X, wp.Y);
                    return new PvVec2(cx, cy);
                })
                .ToList();
        }

        var n1 = PortExitNormalCanvas(start);
        var n2 = PortExitNormalCanvas(end);
        return PvWireRouting.Route(
            new PvVec2(p1.X, p1.Y),
            new PvVec2(n1.X, n1.Y),
            new PvVec2(p2.X, p2.Y),
            new PvVec2(n2.X, n2.Y),
            startPanel,
            endPanel,
            obstacles,
            manual,
            laneOffset);
    }

    private List<PvRect> CollectPanelObstacleRects()
    {
        var list = new List<PvRect>(_panelVisuals.Count);
        foreach (var visual in _panelVisuals.Values)
            list.Add(ToPvRect(PanelCanvasRect(visual, pad: 6)));
        return list;
    }

    private static PvRect ToPvRect(Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private Brush NeutralCableBrush(bool selected = false)
    {
        // Dark-theme charcoal cable; slightly brighter when selected.
        return selected
            ? new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xCC))
            : new SolidColorBrush(Color.FromRgb(0x8B, 0x8B, 0x96));
    }

    private System.Windows.Shapes.Path CreateWirePath(
        PvWireRouteResult route,
        Brush stroke,
        double thickness,
        Guid connectionId,
        bool isHitTarget,
        bool rounded)
    {
        var pts = route.PathPoints.Count > 0
            ? route.PathPoints
            : new[] { route.Start, route.End };

        var geo = rounded && !isHitTarget
            ? BuildRoundedOrthoGeometry(pts, cornerRadius: Math.Clamp(7 * _zoom, 5, 12))
            : BuildPolylineGeometry(pts);

        var path = new System.Windows.Shapes.Path
        {
            Data = geo,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Tag = connectionId,
            // Visible stroke is decorative; fat transparent sibling owns hit-testing.
            IsHitTestVisible = isHitTarget,
            Cursor = Cursors.SizeAll,
        };
        AttachWireInteraction(path, connectionId);
        return path;
    }

    private static PathGeometry BuildPolylineGeometry(IReadOnlyList<PvVec2> pts)
    {
        var geo = new PathGeometry();
        if (pts.Count == 0) return geo;
        var fig = new PathFigure
        {
            StartPoint = new Point(pts[0].X, pts[0].Y),
            IsClosed = false,
        };
        for (var i = 1; i < pts.Count; i++)
            fig.Segments.Add(new LineSegment(new Point(pts[i].X, pts[i].Y), true));
        geo.Figures.Add(fig);
        return geo;
    }

    /// <summary>Orthogonal polyline with short ArcSegment fillets at elbows.</summary>
    private static PathGeometry BuildRoundedOrthoGeometry(IReadOnlyList<PvVec2> pts, double cornerRadius)
    {
        var geo = new PathGeometry();
        if (pts.Count == 0) return geo;
        if (pts.Count < 3)
            return BuildPolylineGeometry(pts);

        var fig = new PathFigure
        {
            StartPoint = new Point(pts[0].X, pts[0].Y),
            IsClosed = false,
        };

        for (var i = 1; i < pts.Count - 1; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var c = pts[i + 1];
            var inDx = b.X - a.X;
            var inDy = b.Y - a.Y;
            var outDx = c.X - b.X;
            var outDy = c.Y - b.Y;
            var inLen = Math.Sqrt(inDx * inDx + inDy * inDy);
            var outLen = Math.Sqrt(outDx * outDx + outDy * outDy);
            if (inLen < 1e-6 || outLen < 1e-6)
            {
                fig.Segments.Add(new LineSegment(new Point(b.X, b.Y), true));
                continue;
            }

            var r = Math.Min(cornerRadius, Math.Min(inLen, outLen) * 0.45);
            if (r < 1.5)
            {
                fig.Segments.Add(new LineSegment(new Point(b.X, b.Y), true));
                continue;
            }

            var inUx = inDx / inLen;
            var inUy = inDy / inLen;
            var outUx = outDx / outLen;
            var outUy = outDy / outLen;
            var before = new Point(b.X - inUx * r, b.Y - inUy * r);
            var after = new Point(b.X + outUx * r, b.Y + outUy * r);
            fig.Segments.Add(new LineSegment(before, true));

            // Screen coords (Y down): positive cross = clockwise turn.
            var cross = inUx * outUy - inUy * outUx;
            var sweep = cross >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
            fig.Segments.Add(new ArcSegment(
                after,
                new Size(r, r),
                0,
                false,
                sweep,
                true));
        }

        var last = pts[^1];
        fig.Segments.Add(new LineSegment(new Point(last.X, last.Y), true));
        geo.Figures.Add(fig);
        return geo;
    }

    private Line CreatePolarityTip(Point port, Vector exit, double length, Brush brush, Guid connectionId)
    {
        var line = new Line
        {
            X1 = port.X,
            Y1 = port.Y,
            X2 = port.X + exit.X * length,
            Y2 = port.Y + exit.Y * length,
            Stroke = brush,
            StrokeThickness = 2.0,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            Tag = connectionId,
        };
        return line;
    }

    private void AddWireShape(UIElement element, int z)
    {
        Panel.SetZIndex(element, z);
        DesignCanvas.Children.Add(element);
    }

    private void ClearPanelPortHitOverlays()
    {
        foreach (var el in _panelPortHitOverlays.Values)
            DesignCanvas.Children.Remove(el);
        _panelPortHitOverlays.Clear();

        foreach (var el in _panelPortLeadVisuals)
            DesignCanvas.Children.Remove(el);
        _panelPortLeadVisuals.Clear();
    }

    private void RebuildPanelPortHitOverlays()
    {
        ClearPanelPortHitOverlays();
        foreach (var panel in _project.Graph.Panels.Values)
        {
            if (!_panelVisuals.TryGetValue(panel.Id, out var visual)) continue;
            AddSimplePanelPortOverlay(panel, visual, panel.NegativePort, positive: false);
            AddSimplePanelPortOverlay(panel, visual, panel.PositivePort, positive: true);
        }
    }

    private void AddSimplePanelPortOverlay(
        SolarPanelInstance panel,
        PanelVisual visual,
        ElectricalPort port,
        bool positive)
    {
        var layout = GetPanelLocalLayout(panel);
        var (rootX, rootY) = WorldToCanvas(panel.PositionXMm, panel.PositionYMm);
        var scale = MmToPx * _zoom;

        var leadStart = positive
            ? new Point(rootX + layout.PosLeadStartXMm * scale, rootY + layout.PosLeadStartYMm * scale)
            : new Point(rootX + layout.NegLeadStartXMm * scale, rootY + layout.NegLeadStartYMm * scale);
        var center = positive
            ? new Point(rootX + layout.PosLocalXMm * scale, rootY + layout.PosLocalYMm * scale)
            : new Point(rootX + layout.NegLocalXMm * scale, rootY + layout.NegLocalYMm * scale);

        var emphasize = _selectedPanelIds.Contains(panel.Id)
                        || visual.Root.IsMouseOver
                        || _wireFromPortId is not null;
        var opacity = emphasize ? 1.0 : 0.55;

        var lead = new Line
        {
            X1 = leadStart.X,
            Y1 = leadStart.Y,
            X2 = center.X,
            Y2 = center.Y,
            Stroke = positive
                ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
                : new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
            StrokeThickness = emphasize ? 1.6 : 1.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = opacity,
            IsHitTestVisible = false,
        };
        Panel.SetZIndex(lead, 955);
        DesignCanvas.Children.Add(lead);
        _panelPortLeadVisuals.Add(lead);

        var hit = PanelPortLayoutService.HitTargetSizePx;
        var fill = positive
            ? new SolidColorBrush(Color.FromRgb(0x2A, 0x16, 0x16))
            : new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A));
        var accent = positive
            ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
            : new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7));

        var glyph = SimplePvTerminalVisual.Create(
            positive,
            port.IsOccupied,
            opacity,
            fill,
            accent);

        var hitTarget = new Border
        {
            Width = hit,
            Height = hit,
            Background = Brushes.Transparent,
            Cursor = Cursors.Cross,
            Tag = port.Id,
            ToolTip = FormatPortTooltip(port),
            Child = glyph,
        };
        Canvas.SetLeft(hitTarget, center.X - hit / 2);
        Canvas.SetTop(hitTarget, center.Y - hit / 2);
        Panel.SetZIndex(hitTarget, 960);
        hitTarget.MouseLeftButtonDown += Port_MouseLeftButtonDown;
        DesignCanvas.Children.Add(hitTarget);
        _panelPortHitOverlays[port.Id] = hitTarget;
    }

    private PanelPortLayoutService.LocalPortLayout GetPanelLocalLayout(SolarPanelInstance panel)
    {
        var def = _project.RequireDefinition(panel.DefinitionId);
        var sizeAt1 = GetPanelSizePx(def, panel.RotationDegrees);
        return PanelPortLayoutService.ForAxisAlignedPanel(
            sizeAt1.Width / MmToPx,
            sizeAt1.Height / MmToPx);
    }

    private Vector PortExitNormalCanvas(ElectricalPort port)
    {
        if (_project.Graph.TryGetPanel(port.OwnerComponentId, out var panel))
        {
            var layout = GetPanelLocalLayout(panel);
            return new Vector(layout.ExitNormalX, layout.ExitNormalY);
        }

        if (_project.Graph.TryGetEquipment(port.OwnerComponentId, out var eq)
            && (ElectricalEquipmentInstance.IsLandscapePrismaticBattery(eq)
                || ElectricalEquipmentInstance.IsRackBattery(eq)
                || ElectricalEquipmentInstance.IsWall10kWBattery(eq)))
            return new Vector(0, -1); // top terminals — exit upward

        // Equipment / unknown: keep prior polarity-based outward guess.
        return port.Polarity == Polarity.Positive ? new Vector(0, -1) : new Vector(0, 1);
    }

    private static Rect PanelCanvasRect(PanelVisual visual, double pad)
    {
        var left = Canvas.GetLeft(visual.Root);
        var top = Canvas.GetTop(visual.Root);
        return new Rect(
            left - pad,
            top - pad,
            visual.Body.Width + pad * 2,
            visual.Body.Height + pad * 2);
    }

    private void AttachWireInteraction(UIElement element, Guid connectionId)
    {
        element.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount >= 2)
            {
                InsertWaypointAtCanvas(connectionId, e.GetPosition(DesignCanvas));
                e.Handled = true;
                return;
            }

            var pos = e.GetPosition(DesignCanvas);
            if (_selectedConnectionIds.Contains(connectionId)
                && TryBeginWireSegmentDrag(connectionId, pos))
            {
                e.Handled = true;
                return;
            }

            SelectConnection(connectionId);
            e.Handled = true;
        };
        element.MouseRightButtonDown += (_, e) =>
        {
            SelectConnection(connectionId);
            ShowCanvasContextMenu(e.GetPosition(DesignCanvas));
            e.Handled = true;
        };
    }

    private void InsertWaypointAtCanvas(Guid connectionId, Point canvasPos)
    {
        if (!_project.Graph.Connections.TryGetValue(connectionId, out var connection))
            return;
        if (!_project.Graph.TryGetPort(connection.StartPortId, out var start)
            || !_project.Graph.TryGetPort(connection.EndPortId, out var end))
            return;

        BakeAutoRouteWaypoints(connection, start, end);

        var (xMm, yMm) = CanvasToWorld(canvasPos);
        var startPt = CanvasToWorld(GetPortCanvasPoint(start));
        var endPt = CanvasToWorld(GetPortCanvasPoint(end));
        var index = WireRouting.InsertWaypointNear(
            connection.Wire.Waypoints,
            new Point2Mm(startPt.xMm, startPt.yMm),
            new Point2Mm(endPt.xMm, endPt.yMm),
            new Point2Mm(xMm, yMm));

        if (index < 0) return;

        _selectedWaypointConnectionId = connectionId;
        _selectedWaypointIndex = index;
        _project.NotifyChanged("Add wire waypoint");
        if (!_selectedConnectionIds.Contains(connectionId))
            SetSelection(connections: new[] { connectionId });
        else
            RefreshAll();
    }

    /// <summary>
    /// Persist current Smart Wiring corners as editable waypoints (excludes port endpoints).
    /// </summary>
    private void BakeAutoRouteWaypoints(
        ElectricalConnection connection,
        ElectricalPort start,
        ElectricalPort end)
    {
        if (connection.Wire.Waypoints.Count > 0) return;

        var p1 = GetPortCanvasPoint(start);
        var p2 = GetPortCanvasPoint(end);
        var route = BuildPvWireRoute(start, end, p1, p2, connection, CollectPanelObstacleRects());
        var pts = route.PathPoints;
        if (pts.Count < 3) return;

        connection.Wire.Waypoints.Clear();
        for (var i = 1; i < pts.Count - 1; i++)
        {
            var (xMm, yMm) = CanvasToWorld(new Point(pts[i].X, pts[i].Y));
            connection.Wire.Waypoints.Add(new Point2Mm(xMm, yMm));
        }
    }

    private bool TryBeginWireSegmentDrag(Guid connectionId, Point canvasPos)
    {
        if (!_project.Graph.Connections.TryGetValue(connectionId, out var connection))
            return false;
        if (!_project.Graph.TryGetPort(connection.StartPortId, out var start)
            || !_project.Graph.TryGetPort(connection.EndPortId, out var end))
            return false;

        BakeAutoRouteWaypoints(connection, start, end);
        if (connection.Wire.Waypoints.Count == 0) return false;

        var points = BuildWireWorldPolyline(connection, start, end);
        var bestSeg = -1;
        var bestDist = 14.0; // canvas px
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = WorldToCanvas(points[i].X, points[i].Y);
            var b = WorldToCanvas(points[i + 1].X, points[i + 1].Y);
            var proj = ProjectPointOntoSegmentCanvas(canvasPos, new Point(a.x, a.y), new Point(b.x, b.y));
            var d = Hypot(canvasPos.X - proj.X, canvasPos.Y - proj.Y);
            if (d < bestDist)
            {
                bestDist = d;
                bestSeg = i;
            }
        }

        if (bestSeg < 0) return false;

        var pa = points[bestSeg];
        var pb = points[bestSeg + 1];
        var horizontal = Math.Abs(pa.Y - pb.Y) <= Math.Abs(pa.X - pb.X);
        // Need at least one waypoint endpoint on this segment.
        if (bestSeg == 0 && bestSeg + 1 == points.Count - 1) return false;

        _draggingWireSegmentConnectionId = connectionId;
        _draggingWireSegmentIndex = bestSeg;
        _draggingWireSegmentHorizontal = horizontal;
        DesignCanvas.CaptureMouse();
        RebuildWireVisuals();
        return true;
    }

    private List<Point2Mm> BuildWireWorldPolyline(
        ElectricalConnection connection,
        ElectricalPort start,
        ElectricalPort end)
    {
        var startPt = CanvasToWorld(GetPortCanvasPoint(start));
        var endPt = CanvasToWorld(GetPortCanvasPoint(end));
        var pts = new List<Point2Mm> { new(startPt.xMm, startPt.yMm) };
        pts.AddRange(connection.Wire.Waypoints);
        pts.Add(new Point2Mm(endPt.xMm, endPt.yMm));
        return pts;
    }

    private static Point ProjectPointOntoSegmentCanvas(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9) return a;
        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0, 1);
        return new Point(a.X + t * dx, a.Y + t * dy);
    }

    private bool ConnectionVisibleInCurrentPlan(ElectricalConnection connection)
    {
        if (!_project.Graph.TryGetPort(connection.StartPortId, out var start)
            || !_project.Graph.TryGetPort(connection.EndPortId, out var end))
            return false;

        var startIsPanel = _project.Graph.Panels.ContainsKey(start.OwnerComponentId);
        var endIsPanel = _project.Graph.Panels.ContainsKey(end.OwnerComponentId);
        var startIsEquipment = _project.Graph.Equipment.ContainsKey(start.OwnerComponentId);
        var endIsEquipment = _project.Graph.Equipment.ContainsKey(end.OwnerComponentId);

        return _workspacePlan switch
        {
            // Roof plan: module-to-module string wiring only.
            WorkspacePlan.Roof => startIsPanel && endIsPanel,
            // Interior: equipment↔equipment only.
            WorkspacePlan.Interior => startIsEquipment && endIsEquipment,
            // Combined: show all DC runs including module↔equipment home-runs.
            WorkspacePlan.Combined => true,
            _ => true,
        };
    }

    private bool ShowsRoofGeometry =>
        _workspacePlan is WorkspacePlan.Roof or WorkspacePlan.Combined;

    private bool ShowsPanels =>
        _workspacePlan is WorkspacePlan.Roof or WorkspacePlan.Combined;

    private bool ShowsEquipment =>
        _workspacePlan is WorkspacePlan.Interior or WorkspacePlan.Combined;

    private Point2Mm GetPortWorldPoint(ElectricalPort port)
    {
        // Prefer live canvas geometry when the owner is visible on this plan.
        if (_panelVisuals.ContainsKey(port.OwnerComponentId)
            || _equipmentVisuals.ContainsKey(port.OwnerComponentId))
        {
            var canvas = GetPortCanvasPoint(port);
            var (xMm, yMm) = CanvasToWorld(canvas);
            return new Point2Mm(xMm, yMm);
        }

        if (_project.Graph.TryGetPanel(port.OwnerComponentId, out var panel))
            return new Point2Mm(panel.PositionXMm, panel.PositionYMm);

        if (_project.Graph.TryGetEquipment(port.OwnerComponentId, out var equipment))
            return new Point2Mm(equipment.PositionXMm, equipment.PositionYMm);

        return new Point2Mm(0, 0);
    }

    private void DisconnectWire(Guid connectionId)
    {
        if (!_project.Graph.Connections.ContainsKey(connectionId))
            return;

        _selectedConnectionIds.Remove(connectionId);
        if (_selectedWaypointConnectionId == connectionId)
        {
            _selectedWaypointConnectionId = null;
            _selectedWaypointIndex = null;
        }

        _project.History.Execute(new DisconnectCommand(_project, connectionId));
        RefreshAll();
    }

    private void RoofPlan_Click(object sender, RoutedEventArgs e) => SetWorkspacePlan(WorkspacePlan.Roof);

    private void InteriorPlan_Click(object sender, RoutedEventArgs e) => SetWorkspacePlan(WorkspacePlan.Interior);

    private void CombinedPlan_Click(object sender, RoutedEventArgs e) => SetWorkspacePlan(WorkspacePlan.Combined);

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var next = ThemeController.Current == ThemeController.ThemeKind.DarkCad
            ? ThemeController.ThemeKind.LightAtelier
            : ThemeController.ThemeKind.DarkCad;
        ThemeController.Apply(next);
        ThemeToggleButton.Content = "◐";
        ThemeToggleButton.ToolTip = next == ThemeController.ThemeKind.DarkCad
            ? "Switch to light theme"
            : "Switch to dark theme";
        ApplyWorkspacePlanUi();
        RefreshAll();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        if (MaximizeButton is not null)
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ProjectSettings_Click(object sender, RoutedEventArgs e)
    {
        SetSelection();
        SetInspectorOpen(true);
        if (SiteTempsPanel is not null)
            SiteTempsPanel.Visibility = Visibility.Visible;
        if (RackingPanel is not null)
            RackingPanel.Visibility = Visibility.Visible;
        InspectorHeading.Text = "Project";
        if (InspectorSubheading is not null)
            InspectorSubheading.Text = "Site · climate · energy";
        ClearInspectorRows();
        AddInspectorSection("Settings");
        AddInspectorNote("Edit site and racking fields below. These stay hidden while a canvas object is selected.");
    }

    private void OverflowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (OverflowMenuButton.ContextMenu is null) return;
        var target = sender as UIElement ?? OverflowMenuButton;
        OverflowMenuButton.ContextMenu.PlacementTarget = target;
        OverflowMenuButton.ContextMenu.IsOpen = true;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AboutDialog(GetAppVersion()) { Owner = this };
        dlg.ShowDialog();
    }

    private static string GetAppVersion()
    {
        var asm = typeof(MainWindow).Assembly;
        var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "0.1.1" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private void SetWorkspacePlan(WorkspacePlan plan)
    {
        if (_workspacePlan == plan)
        {
            ApplyWorkspacePlanUi();
            return;
        }

        _workspacePlan = plan;
        _tool = CanvasTool.Select;
        CancelWireDrag();
        ClearRoofLiveMeasure();
        _draggingRoofVertexIndex = null;
        _selectedObstacleId = null;
        _selectedWaypointConnectionId = null;
        _selectedWaypointIndex = null;

        switch (plan)
        {
            case WorkspacePlan.Roof:
                _selectedEquipmentIds.Clear();
                _layersCategory = LayersCategory.Roofs;
                break;
            case WorkspacePlan.Interior:
                _selectedPanelIds.Clear();
                _selectedObstacleId = null;
                _layersCategory = LayersCategory.Equipment;
                if (_uiTool is UiTool.Roof or UiTool.Panel or UiTool.Obstacle)
                    _uiTool = UiTool.Add;
                break;
            case WorkspacePlan.Combined:
                if (_layersCategory == LayersCategory.Equipment && _project.Graph.Equipment.Count == 0)
                    _layersCategory = LayersCategory.Roofs;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(plan), plan, null);
        }

        _selectedConnectionIds.RemoveWhere(id =>
            !_project.Graph.Connections.TryGetValue(id, out var c) || !ConnectionVisibleInCurrentPlan(c));

        ApplyWorkspacePlanUi();
        RefreshAll();
    }

    private void ApplyWorkspacePlanUi()
    {
        var showRoofTools = ShowsRoofGeometry;
        var showPanelsLib = ShowsPanels;
        var showEquipLib = ShowsEquipment;

        // Category visibility is applied when opening Add (RefreshAddPalette).
        RoofToolsPanel.Visibility = Visibility.Collapsed;
        RackingPanel.Visibility = showRoofTools ? Visibility.Visible : Visibility.Collapsed;
        SiteTempsPanel.Visibility = showEquipLib || _workspacePlan == WorkspacePlan.Combined
            ? Visibility.Visible
            : Visibility.Collapsed;

        LayersRoofsTab.Visibility = showRoofTools ? Visibility.Visible : Visibility.Collapsed;
        LayersPanelsTab.Visibility = showPanelsLib ? Visibility.Visible : Visibility.Collapsed;
        LayersEquipmentTab.Visibility = showEquipLib ? Visibility.Visible : Visibility.Collapsed;
        RoofLayerActions.Visibility = showRoofTools ? Visibility.Visible : Visibility.Collapsed;

        StylePlanTab(RoofPlanButton, _workspacePlan == WorkspacePlan.Roof);
        StylePlanTab(InteriorPlanButton, _workspacePlan == WorkspacePlan.Interior);
        StylePlanTab(CombinedPlanButton, _workspacePlan == WorkspacePlan.Combined);

        if (_workspacePlan == WorkspacePlan.Combined)
            SetInspectorOpen(true);

        UpdateToolRailStyles();
        UpdateContextToolbar();
    }

    private void StylePlanTab(Button button, bool active)
    {
        button.Tag = active ? "Active" : null;
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        button.Foreground = active
            ? (Brush)FindResource("TextBrush")
            : (Brush)FindResource("MutedBrush");
        button.Background = Brushes.Transparent;
    }

    private void UpdateEmptyState()
    {
        switch (_workspacePlan)
        {
            case WorkspacePlan.Roof:
            {
                var empty = _project.Graph.Panels.Count == 0 && !HasAnyRoofVertices();
                EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                EmptyStateTitle.Text = "Start with your roof";
                EmptyStateBody.Text = "Draw it, or trace it free on the satellite map.";
                EmptyStateButton.Content = "Trace on map";
                break;
            }
            case WorkspacePlan.Interior:
            {
                var empty = _project.Graph.Equipment.Count == 0;
                EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                EmptyStateTitle.Text = "Add equipment";
                EmptyStateBody.Text = "Place combiners, inverters, and batteries.";
                EmptyStateButton.Content = "Add Inverter";
                break;
            }
            case WorkspacePlan.Combined:
            {
                var empty = _project.Graph.Panels.Count == 0
                    && _project.Graph.Equipment.Count == 0
                    && !HasAnyRoofVertices();
                EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                EmptyStateTitle.Text = "Build your system";
                EmptyStateBody.Text = "Roof modules and equipment on one canvas.";
                EmptyStateButton.Content = "Trace on map";
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void EmptyStateDraw_Click(object sender, RoutedEventArgs e)
    {
        SetUiTool(UiTool.Roof);
        DrawRoof_Click(sender, e);
    }

    private void EmptyStateImport_Click(object sender, RoutedEventArgs e)
    {
        if (_workspacePlan == WorkspacePlan.Interior)
        {
            SetUiTool(UiTool.Add);
            AddInverter5k_Click(sender, e);
            return;
        }

        SatelliteMap_Click(sender, e);
    }

    private void EmptyStateButton_Click(object sender, RoutedEventArgs e) =>
        EmptyStateImport_Click(sender, e);

    private string FriendlyProjectName()
    {
        var name = string.IsNullOrWhiteSpace(_project.Name) ? "Untitled" : _project.Name.Trim();
        if (name.EndsWith(".solarproj", StringComparison.OrdinalIgnoreCase))
            name = name[..^".solarproj".Length];
        // Strip legacy autosave GUID suffix only: Name_<32 hex chars>
        var underscore = name.LastIndexOf('_');
        if (underscore > 0)
        {
            var suffix = name[(underscore + 1)..];
            if (suffix.Length == 32 && suffix.All(IsHexChar))
                name = name[..underscore];
        }
        // Drop legacy " Project" suffix from older defaults
        if (name.EndsWith(" Project", StringComparison.OrdinalIgnoreCase))
            name = name[..^" Project".Length].TrimEnd();
        return string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
    }

    private static bool IsHexChar(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private void ToolSelect_Click(object sender, RoutedEventArgs e) => SetUiTool(UiTool.Select);
    private void ToolRoof_Click(object sender, RoutedEventArgs e) => SetUiTool(UiTool.Roof);
    private void ToolPanel_Click(object sender, RoutedEventArgs e) => SetUiTool(UiTool.Panel);
    private void ToolWire_Click(object sender, RoutedEventArgs e) => SetUiTool(UiTool.Wire);
    private void ToolObstacle_Click(object sender, RoutedEventArgs e)
    {
        SetUiTool(UiTool.Obstacle);
        AddObstacleMode_Click(sender, e);
    }

    private void ToolMeasure_Click(object sender, RoutedEventArgs e)
    {
        SetUiTool(UiTool.Measure);
        _tool = CanvasTool.Measure;
        ClearMeasureTool();
        StatusText.Text = "MEASURE  ·  Click points for live edge lengths (Esc clears)";
    }

    private void SetUiTool(UiTool tool)
    {
        _uiTool = tool;
        if (tool != UiTool.Obstacle && _tool == CanvasTool.PlaceObstacle)
            _tool = CanvasTool.Select;
        if (tool != UiTool.Measure && _tool == CanvasTool.Measure)
        {
            ClearMeasureTool();
            _tool = CanvasTool.Select;
        }
        if (tool == UiTool.Select)
            _tool = CanvasTool.Select;
        if (tool == UiTool.Measure)
            _tool = CanvasTool.Measure;

        UpdateToolRailStyles();
        UpdateContextToolbar();
        UpdateToolButtonStyles();
    }

    private void ToolAdd_Click(object sender, RoutedEventArgs e) => SetUiTool(UiTool.Add);

    private void UpdateToolRailStyles()
    {
        StyleToolRail(ToolSelectButton, _uiTool == UiTool.Select);
        StyleToolRail(ToolRoofButton, _uiTool == UiTool.Roof);
        StyleToolRail(ToolPanelButton, _uiTool == UiTool.Panel);
        StyleToolRail(ToolWireButton, _uiTool == UiTool.Wire);
        StyleToolRail(ToolObstacleButton, _uiTool == UiTool.Obstacle);
        StyleToolRail(ToolMeasureButton, _uiTool == UiTool.Measure);
        StyleToolRail(ToolAddButton, _uiTool == UiTool.Add);
        StyleToolRail(LayersRailButton, _uiTool == UiTool.Layers);

        // Equipment plan: only equipment tools — hide roof / panel / obstacle chrome.
        var roofish = ShowsRoofGeometry;
        var panels = ShowsPanels;
        if (ToolRoofButton is not null)
            ToolRoofButton.Visibility = roofish ? Visibility.Visible : Visibility.Collapsed;
        if (ToolPanelButton is not null)
            ToolPanelButton.Visibility = panels ? Visibility.Visible : Visibility.Collapsed;
        if (ToolObstacleButton is not null)
            ToolObstacleButton.Visibility = roofish ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void StyleToolRail(Button? button, bool active)
    {
        if (button is null) return;
        button.Tag = active ? "Active" : null;
    }

    private void UpdateContextToolbar()
    {
        // Hide all side-panel sections first.
        SetPanelVisible(RoofContextPanel, false);
        SetPanelVisible(PanelContextPanel, false);
        SetPanelVisible(WireContextPanel, false);
        SetPanelVisible(ObstacleContextPanel, false);
        SetPanelVisible(AddPalette, false);
        SetPanelVisible(LayersDrawer, false);
        SetPanelVisible(MeasureContextPanel, false);
        SetPanelVisible(ImportRoofPanel, false);

        var openSide = _uiTool is not UiTool.Select;
        SetSidePanelOpen(openSide);

        switch (_uiTool)
        {
            case UiTool.Select:
                break;
            case UiTool.Roof:
                ContextTitle.Text = "Roof";
                SetPanelVisible(RoofContextPanel, true);
                break;
            case UiTool.Panel:
                ContextTitle.Text = "Panels";
                SetPanelVisible(PanelContextPanel, true);
                break;
            case UiTool.Wire:
                ContextTitle.Text = "Wire";
                SetPanelVisible(WireContextPanel, true);
                break;
            case UiTool.Obstacle:
                ContextTitle.Text = "Object";
                SetPanelVisible(ObstacleContextPanel, true);
                break;
            case UiTool.Measure:
                ContextTitle.Text = "Measure";
                SetPanelVisible(MeasureContextPanel, true);
                break;
            case UiTool.Add:
                ContextTitle.Text = "Add";
                SetPanelVisible(AddPalette, true);
                RefreshAddPalette();
                break;
            case UiTool.Layers:
                ContextTitle.Text = "Layers";
                SetPanelVisible(LayersDrawer, true);
                RefreshLayersPanel();
                break;
            default:
                break;
        }
    }

    private void UpdateSelectionToolbar()
    {
        if (ContextToolBar is null) return;

        var show = _selectedPanelIds.Count > 0
                   && _selectedConnectionIds.Count == 0
                   && _selectedEquipmentIds.Count == 0
                   && _uiTool == UiTool.Select;

        if (!show)
        {
            ContextToolBar.Visibility = Visibility.Collapsed;
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var id in _selectedPanelIds)
        {
            if (!_panelVisuals.TryGetValue(id, out var v)) continue;
            var left = Canvas.GetLeft(v.Root);
            var top = Canvas.GetTop(v.Root);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;
            any = true;
            minX = Math.Min(minX, left);
            minY = Math.Min(minY, top);
            maxX = Math.Max(maxX, left + v.Root.Width);
            maxY = Math.Max(maxY, top + v.Root.Height);
        }

        if (!any)
        {
            ContextToolBar.Visibility = Visibility.Collapsed;
            return;
        }

        ContextToolBar.Visibility = Visibility.Visible;
        ContextToolBar.UpdateLayout();
        var tw = ContextToolBar.ActualWidth > 1 ? ContextToolBar.ActualWidth : 200;
        var leftMargin = Math.Max(8, (minX + maxX) / 2 - tw / 2);
        var topMargin = Math.Max(8, minY - 44);
        ContextToolBar.Margin = new Thickness(leftMargin, topMargin, 0, 0);

        if (CtxStringButton is not null)
        {
            var strings = _project.Graph.Strings
                .Where(s => s.PanelIdsInSeriesOrder.Any(id => _selectedPanelIds.Contains(id)))
                .ToList();
            CtxStringButton.IsEnabled = strings.Count > 0;
            CtxStringButton.Content = strings.Count == 1
                ? strings[0].DisplayName
                : strings.Count > 1 ? $"String ({strings.Count})" : "String";
            CtxStringButton.Tag = strings;
        }
    }

    private void CtxRotate_Click(object sender, RoutedEventArgs e)
    {
        foreach (var rotateId in _selectedPanelIds.ToList())
        {
            var panel = _project.Graph.GetPanel(rotateId);
            _project.History.Execute(new RotatePanelCommand(
                _project, rotateId, panel.RotationDegrees, panel.RotationDegrees + 90));
        }
        RefreshAll();
    }

    private void CtxDuplicate_Click(object sender, RoutedEventArgs e) => DuplicateSelectedPanels();

    private void DuplicateSelectedPanels()
    {
        if (_selectedPanelIds.Count == 0) return;
        var commands = _selectedPanelIds
            .Select(id => (SolarSim.Application.Commands.ICommand)new DuplicatePanelCommand(_project, id))
            .ToList();
        _project.History.Execute(new CompositeCommand(
            commands.Count == 1 ? "Duplicated panel" : $"Duplicated {commands.Count} panels",
            commands));
        RefreshAll();
        StatusText.Text = $"Duplicated {commands.Count} panel(s)";
    }

    private void CopySelectedPanels()
    {
        if (_selectedPanelIds.Count == 0)
        {
            StatusText.Text = "Nothing to copy — select panel(s) first";
            return;
        }

        var panels = _selectedPanelIds
            .Where(id => _project.Graph.Panels.ContainsKey(id))
            .Select(id => _project.Graph.GetPanel(id))
            .ToList();
        if (panels.Count == 0) return;

        var ox = panels.Min(p => p.PositionXMm);
        var oy = panels.Min(p => p.PositionYMm);
        _panelClipboard = panels
            .Select(p => new PanelClipboardItem(
                p.DefinitionId,
                p.PositionXMm - ox,
                p.PositionYMm - oy,
                p.RotationDegrees))
            .ToList();
        StatusText.Text = $"Copied {_panelClipboard.Count} panel(s) — Ctrl+V or right-click → Paste";
    }

    private void PastePanelsNearViewCenter()
    {
        var viewW = DesignCanvas.ActualWidth;
        var viewH = DesignCanvas.ActualHeight;
        if (viewW < 20 || viewH < 20)
        {
            PastePanelsAt(new Point2Mm(0, 0));
            return;
        }

        var (xMm, yMm) = CanvasToWorld(new Point(viewW / 2, viewH / 2));
        PastePanelsAt(new Point2Mm(xMm, yMm));
    }

    private void PastePanelsAt(Point2Mm originMm)
    {
        if (_panelClipboard is null || _panelClipboard.Count == 0)
        {
            StatusText.Text = "Clipboard empty — copy panels first (Ctrl+C)";
            return;
        }

        var panels = _panelClipboard
            .Select(item => new SolarPanelInstance(
                Guid.NewGuid(),
                item.DefinitionId,
                originMm.X + item.OffsetXMm,
                originMm.Y + item.OffsetYMm,
                item.RotationDegrees))
            .ToList();

        var commands = panels
            .Select(panel => (SolarSim.Application.Commands.ICommand)new AddPanelCommand(_project, panel))
            .ToList();

        _project.History.Execute(new CompositeCommand(
            panels.Count == 1 ? "Pasted panel" : $"Pasted {panels.Count} panels",
            commands));

        SetSelection(panels: panels.Select(p => p.Id).ToList());
        RefreshAll();
        StatusText.Text = $"Pasted {panels.Count} panel(s)";
    }

    private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(DesignCanvas);
        _contextMenuCanvasPoint = pos;

        // Canva-style: right-click an unselected object selects it, then shows the menu.
        if (FindPanelAt(pos) is Guid panelId)
        {
            if (!_selectedPanelIds.Contains(panelId))
                SetSelection(panels: new[] { panelId });
        }
        else if (FindEquipmentAt(pos) is Guid equipmentId)
        {
            if (!_selectedEquipmentIds.Contains(equipmentId))
            {
                ClearTransientSelection();
                _selectedEquipmentIds.Add(equipmentId);
                RefreshAll();
            }
        }
        else if (FindObstacleAt(pos) is Guid obstacleId)
        {
            _selectedObstacleId = obstacleId;
            _selectedPanelIds.Clear();
            _selectedConnectionIds.Clear();
            _selectedEquipmentIds.Clear();
            RefreshAll();
        }

        ShowCanvasContextMenu(pos);
        e.Handled = true;
    }

    private void ShowCanvasContextMenu(Point canvasPos)
    {
        var menu = new ContextMenu();
        var hasPanels = _selectedPanelIds.Count > 0;
        var hasClipboard = _panelClipboard is { Count: > 0 };
        var hasConnections = _selectedConnectionIds.Count > 0;
        var hasEquipment = _selectedEquipmentIds.Count > 0;
        var hasRoof = _project.Roofs.Roofs.Any(r => r.HasRoof);
        var hasSelection = hasPanels || hasConnections || hasEquipment || _selectedObstacleId is not null;

        void Add(string header, Action action, bool enabled = true, string? gesture = null)
        {
            var item = new MenuItem
            {
                Header = header,
                IsEnabled = enabled,
                InputGestureText = gesture,
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        void Sep() => menu.Items.Add(new Separator());

        Add("Copy", CopySelectedPanels, hasPanels, "Ctrl+C");
        Add("Paste here", () =>
        {
            var (xMm, yMm) = CanvasToWorld(canvasPos);
            PastePanelsAt(new Point2Mm(xMm, yMm));
        }, hasClipboard, "Ctrl+V");
        Add("Duplicate", DuplicateSelectedPanels, hasPanels, "Ctrl+D");
        Sep();

        Add("Rotate 90°", () =>
        {
            foreach (var id in _selectedPanelIds.ToList())
            {
                var panel = _project.Graph.GetPanel(id);
                _project.History.Execute(new RotatePanelCommand(
                    _project, id, panel.RotationDegrees, panel.RotationDegrees + 90));
            }
            RefreshAll();
        }, hasPanels, "R");

        var stringHits = _project.Graph.Strings
            .Where(s => s.PanelIdsInSeriesOrder.Any(id => _selectedPanelIds.Contains(id)))
            .ToList();
        if (stringHits.Count == 1)
        {
            var s = stringHits[0];
            Add($"Select string ({s.DisplayName})", () => SetSelection(panels: s.PanelIdsInSeriesOrder));
        }
        else if (stringHits.Count > 1)
        {
            var sub = new MenuItem { Header = "Select string" };
            foreach (var s in stringHits)
            {
                var capture = s;
                var mi = new MenuItem { Header = $"{capture.DisplayName}  ·  {capture.PanelIdsInSeriesOrder.Count} mod" };
                mi.Click += (_, _) => SetSelection(panels: capture.PanelIdsInSeriesOrder);
                sub.Items.Add(mi);
            }
            menu.Items.Add(sub);
        }

        if (hasConnections)
        {
            Sep();
            Add("Disconnect wire", () =>
            {
                foreach (var id in _selectedConnectionIds.ToList())
                {
                    if (_project.Graph.Connections.ContainsKey(id))
                        _project.History.Execute(new DisconnectCommand(_project, id));
                }
                ClearTransientSelection();
                RefreshAll();
            });
        }

        if (hasRoof)
        {
            Sep();
            var anyUnlocked = _project.Roofs.Roofs.Any(r => r.HasRoof && !r.IsLocked);
            Add(anyUnlocked ? "Lock roof" : "Unlock roof", () => ToggleRoofLock_Click(this, new RoutedEventArgs()));
            Add("Straighten edges", () => StraightenRoof_Click(this, new RoutedEventArgs()));
            Add("Rotate roof 15°", () => RotateRoof15_Click(this, new RoutedEventArgs()));
            Add("Frame roofs", () =>
            {
                FrameRoofsInView();
                RefreshAll();
            });
        }

        Sep();
        Add("Select all panels", () =>
        {
            SetSelection(panels: _project.Graph.Panels.Keys);
        }, _project.Graph.Panels.Count > 0, "Ctrl+A");
        Add("Frame selection", FrameSelectionInView, hasPanels || hasEquipment);
        Add("Delete", () => DeleteSelection(), hasSelection, "Del");

        menu.IsOpen = true;
    }

    private void FrameSelectionInView()
    {
        var points = new List<Point2Mm>();
        foreach (var id in _selectedPanelIds)
        {
            if (!_project.Graph.TryGetPanel(id, out var panel)) continue;
            var def = _project.RequireDefinition(panel.DefinitionId);
            var size = GetLogicalSizeMm(def, panel.RotationDegrees);
            points.Add(new Point2Mm(panel.PositionXMm, panel.PositionYMm));
            points.Add(new Point2Mm(panel.PositionXMm + size.width, panel.PositionYMm + size.height));
        }

        foreach (var id in _selectedEquipmentIds)
        {
            if (!_project.Graph.TryGetEquipment(id, out var eq)) continue;
            points.Add(new Point2Mm(eq.PositionXMm, eq.PositionYMm));
            points.Add(new Point2Mm(eq.PositionXMm + eq.WidthMm, eq.PositionYMm + eq.HeightMm));
        }

        if (points.Count == 0)
        {
            FrameRoofsInView();
            RefreshAll();
            return;
        }

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        var widthMm = Math.Max(maxX - minX, 500);
        var heightMm = Math.Max(maxY - minY, 500);
        var viewW = Math.Max(DesignCanvas.ActualWidth, 400);
        var viewH = Math.Max(DesignCanvas.ActualHeight, 300);
        const double padPx = 80;
        var zoomX = (viewW - 2 * padPx) / (widthMm * MmToPx);
        var zoomY = (viewH - 2 * padPx) / (heightMm * MmToPx);
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.15, 3.0);
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        _panOffset = new Point(
            viewW / 2 - cx * MmToPx * _zoom,
            viewH / 2 - cy * MmToPx * _zoom);
        if (ZoomLabel is not null)
            ZoomLabel.Text = $"{_zoom * 100:0}%";
        RefreshAll();
    }

    private void CtxString_Click(object sender, RoutedEventArgs e)
    {
        if (CtxStringButton?.Tag is not IEnumerable<PVString> strings) return;
        var list = strings.ToList();
        if (list.Count == 0) return;

        if (list.Count == 1)
        {
            SetSelection(panels: list[0].PanelIdsInSeriesOrder);
            return;
        }

        var menu = new ContextMenu();
        foreach (var s in list)
        {
            var item = new MenuItem { Header = $"{s.DisplayName}  ·  {s.PanelIdsInSeriesOrder.Count} mod" };
            var capture = s;
            item.Click += (_, _) => SetSelection(panels: capture.PanelIdsInSeriesOrder);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = CtxStringButton;
        menu.IsOpen = true;
    }

    private void RefreshAddPalette()
    {
        var q = (AddSearchBox?.Text ?? "").Trim();
        PopulateAddCategory(AddSolarTiles, AddSolarSection, "Solar", q, ShowsPanels);
        PopulateAddCategory(AddElectricalTiles, AddElectricalSection, "Electrical", q, ShowsEquipment);
        PopulateAddCategory(AddStructuralTiles, AddStructuralSection, "Structural", q, ShowsRoofGeometry);

        if (AddRecentSection is not null)
        {
            AddRecentTiles?.Children.Clear();
            foreach (var key in _recentAddKeys.Take(6))
            {
                if (!TryGetAddCatalogItem(key, out var item)) continue;
                if (item.Category == "Solar" && !ShowsPanels) continue;
                if (item.Category == "Electrical" && !ShowsEquipment) continue;
                if (item.Category == "Structural" && !ShowsRoofGeometry) continue;
                AddRecentTiles?.Children.Add(CreateAddTile(item));
            }
            AddRecentSection.Visibility = string.IsNullOrEmpty(q) && (AddRecentTiles?.Children.Count ?? 0) > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void RememberAdd(string key)
    {
        _recentAddKeys.Remove(key);
        _recentAddKeys.Insert(0, key);
        if (_recentAddKeys.Count > 8)
            _recentAddKeys.RemoveRange(8, _recentAddKeys.Count - 8);
        if (_uiTool == UiTool.Add)
            RefreshAddPalette();
    }

    private sealed record AddCatalogItem(
        string Key,
        string Title,
        string Subtitle,
        string Glyph,
        string Category,
        Action Invoke,
        string? ImageAsset = null);

    private IEnumerable<AddCatalogItem> GetAddCatalog()
    {
        yield return new("boviet", "Module", "Boviet · 270 W", "▦", "Solar", () => AddBoviet_Click(this, new RoutedEventArgs()));
        yield return new("g400", "Module", "Generic · 400 W", "▦", "Solar", () => AddGeneric400_Click(this, new RoutedEventArgs()));
        yield return new("g550", "Module", "Generic · 550 W", "▦", "Solar", () => AddGeneric550_Click(this, new RoutedEventArgs()));
        yield return new("custom", "Module", "Custom panel…", "▦", "Solar", () => AddCustom_Click(this, new RoutedEventArgs()));
        yield return new("combiner", "Combiner", "6-string", "▤", "Electrical", () => AddCombiner_Click(this, new RoutedEventArgs()), "combiner-6string.png");
        yield return new("disconnect", "DC isolator", "Set Amp rating", "⏻", "Electrical", () => AddDisconnect_Click(this, new RoutedEventArgs()), "disconnect-pv-isolator.png");
        yield return new("ypos", "MC4 Y+", "Positive branch", "Y+", "Electrical", () => AddBranchYPos_Click(this, new RoutedEventArgs()));
        yield return new("yneg", "MC4 Y−", "Negative branch", "Y−", "Electrical", () => AddBranchYNeg_Click(this, new RoutedEventArgs()));
        yield return new("inv5", "Inverter", "5 kW string", "◇", "Electrical", () => AddInverter5k_Click(this, new RoutedEventArgs()));
        yield return new("inv76", "Inverter", "7.6 kW string", "◇", "Electrical", () => AddInverter76k_Click(this, new RoutedEventArgs()));
        yield return new("inv42", "Inverter", "ANENJI · 4.2 kW hybrid", "◇", "Electrical", () => AddInverterAnenji4_2k_Click(this, new RoutedEventArgs()), "inverter-anenji-4_2kw.png");
        yield return new("inv65", "Inverter", "ANENJI · 6.5 kW hybrid", "◇", "Electrical", () => AddInverterAnenji6_5k_Click(this, new RoutedEventArgs()), "inverter-anenji-6_5kw.png");
        yield return new("inv12", "Inverter", "ANENJI · 12 kW hybrid", "◇", "Electrical", () => AddInverterAnenji12k_Click(this, new RoutedEventArgs()), "inverter-anenji-12kw.png");
        yield return new("battery", "Battery", "ANENJI · 16 kWh", "▣", "Electrical", () => AddBattery_Click(this, new RoutedEventArgs()), "battery-anenji-16kwh.png");
        yield return new("batt10k", "Battery", "ANENJI · 10 kW wall", "▣", "Electrical", () => AddBattery10kW_Click(this, new RoutedEventArgs()), "battery-anenji-10kw.png");
        yield return new("battrack", "Battery", "ANENJI · 5.1 kWh rack", "▣", "Electrical", () => AddBatteryRack_Click(this, new RoutedEventArgs()), "battery-anenji-5_1kwh-rack.png");
        yield return new("batt128", "Battery", "ANENJI · 12.8V 300Ah", "▣", "Electrical", () => AddBattery12_8V_Click(this, new RoutedEventArgs()), "battery-anenji-12_8v-300ah.png");
        yield return new("batdisc", "Batt disconnect", "Set Amp + wire", "⏻", "Electrical", () => AddBatteryDisconnect_Click(this, new RoutedEventArgs()), "battery-disconnect-dhm1b.png");
        yield return new("acdisc", "AC disconnect", "AC side", "⏻", "Electrical", () => AddAcDisconnect_Click(this, new RoutedEventArgs()));
        yield return new("aclc", "Load center", "AC panel", "☰", "Electrical", () => AddAcLoadCenter_Click(this, new RoutedEventArgs()));
        yield return new("vent", "Roof vent", "Obstacle", "◇", "Structural", () => AddObstacleMode_Click(this, new RoutedEventArgs()));
    }

    private bool TryGetAddCatalogItem(string key, out AddCatalogItem item)
    {
        item = GetAddCatalog().FirstOrDefault(i => i.Key == key)!;
        return item is not null;
    }

    private Button CreateAddTile(AddCatalogItem item)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (!string.IsNullOrWhiteSpace(item.ImageAsset))
        {
            try
            {
                var bmp = LoadEquipmentFaceBitmap(item.ImageAsset!);
                stack.Children.Add(new Image
                {
                    Source = bmp,
                    Width = 52,
                    Height = 40,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }
            catch
            {
                stack.Children.Add(new TextBlock
                {
                    Text = item.Glyph,
                    FontSize = 18,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 4),
                });
            }
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = item.Glyph,
                FontSize = 18,
                Foreground = (Brush)FindResource("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4),
            });
        }

        var tileW = !string.IsNullOrWhiteSpace(item.ImageAsset) ? 108.0 : 96.0;
        var textMax = tileW - 12;

        stack.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            MaxWidth = textMax,
        });
        stack.Children.Add(new TextBlock
        {
            Text = item.Subtitle,
            FontSize = 9.5,
            Foreground = (Brush)FindResource("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = textMax,
            LineHeight = 12,
        });

        var btn = new Button
        {
            Style = (Style)FindResource("AddTileButton"),
            Content = stack,
            Tag = item.Key,
            Width = tileW,
            Height = double.NaN,
            MinHeight = !string.IsNullOrWhiteSpace(item.ImageAsset) ? 108 : 86,
            Padding = new Thickness(6, 6, 6, 7),
            ToolTip = $"{item.Title} — {item.Subtitle}",
        };
        btn.Click += (_, _) =>
        {
            RememberAdd(item.Key);
            item.Invoke();
        };
        return btn;
    }

    private void AddSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshAddPalette();

    private void PopulateAddCategory(
        WrapPanel? panel,
        FrameworkElement? section,
        string category,
        string query,
        bool allowed)
    {
        if (panel is null) return;
        panel.Children.Clear();
        if (!allowed)
        {
            if (section is not null) section.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var item in GetAddCatalog().Where(i => i.Category == category))
        {
            if (!string.IsNullOrEmpty(query))
            {
                var hay = $"{item.Title} {item.Subtitle} {item.Category}".ToLowerInvariant();
                if (!hay.Contains(query.ToLowerInvariant())) continue;
            }
            panel.Children.Add(CreateAddTile(item));
        }
        if (section is not null)
            section.Visibility = panel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetPanelVisible(UIElement? element, bool visible)
    {
        if (element is null) return;
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetSidePanelOpen(bool open)
    {
        if (SidePanelColumn is null || SidePanel is null) return;
        SidePanelColumn.Width = open ? new GridLength(288) : new GridLength(0);
        SidePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetInspectorOpen(bool open)
    {
        if (InspectorColumn is null || InspectorPanel is null) return;
        InspectorColumn.Width = open ? new GridLength(300) : new GridLength(0);
        InspectorPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseSidePanel_Click(object sender, RoutedEventArgs e) => SetUiTool(UiTool.Select);

    private void ToggleInspector_Click(object sender, RoutedEventArgs e)
    {
        var open = InspectorPanel?.Visibility != Visibility.Visible;
        SetInspectorOpen(open);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(1.15);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(1 / 1.15);

    private void ZoomAtCenter(double factor)
    {
        var host = DesignCanvas.Parent as FrameworkElement;
        var cx = (host?.ActualWidth ?? 800) / 2;
        var cy = (host?.ActualHeight ?? 600) / 2;
        var (beforeX, beforeY) = CanvasToWorld(new Point(cx, cy));
        _zoom = Math.Clamp(_zoom * factor, 0.25, 4.0);
        var after = WorldToCanvas(beforeX, beforeY);
        _panOffset.X += cx - after.x;
        _panOffset.Y += cy - after.y;
        RefreshAll();
    }

    private void ShowImportRoof_Click(object sender, RoutedEventArgs e) =>
        SatelliteMap_Click(sender, e);

    private void LayersDrawerToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_uiTool == UiTool.Layers)
            SetUiTool(UiTool.Select);
        else
            SetUiTool(UiTool.Layers);
    }

    private Point GetPortCanvasPoint(ElectricalPort port)
    {
        if (_project.Graph.TryGetPanel(port.OwnerComponentId, out var panel)
            && _panelVisuals.ContainsKey(panel.Id))
        {
            var layout = GetPanelLocalLayout(panel);
            var (rootX, rootY) = WorldToCanvas(panel.PositionXMm, panel.PositionYMm);
            var scale = MmToPx * _zoom;
            var positive = port.Polarity == Polarity.Positive;
            var lx = positive ? layout.PosLocalXMm : layout.NegLocalXMm;
            var ly = positive ? layout.PosLocalYMm : layout.NegLocalYMm;
            return new Point(rootX + lx * scale, rootY + ly * scale);
        }

        if (_equipmentVisuals.TryGetValue(port.OwnerComponentId, out var eqVisual)
            && eqVisual.PortEllipses.TryGetValue(port.Id, out var eqEllipse))
        {
            try
            {
                var local = new Point(eqEllipse.Width / 2, eqEllipse.Height / 2);
                return eqEllipse.TranslatePoint(local, DesignCanvas);
            }
            catch
            {
                var left = Canvas.GetLeft(eqVisual.Root) + Canvas.GetLeft(eqEllipse) + eqEllipse.Width / 2;
                var top = Canvas.GetTop(eqVisual.Root) + Canvas.GetTop(eqEllipse) + eqEllipse.Height / 2;
                return new Point(left, top);
            }
        }

        return new Point();
    }

    private static Size GetPanelSizePx(SolarPanelDefinition def, int rotationDegrees)
    {
        var w = def.WidthMm * MmToPx;
        var h = def.HeightMm * MmToPx;
        var rot = ((rotationDegrees % 180) + 180) % 180;
        return rot == 90 ? new Size(h, w) : new Size(w, h);
    }

    private (double x, double y) WorldToCanvas(double xMm, double yMm) =>
        (_panOffset.X + xMm * MmToPx * _zoom, _panOffset.Y + yMm * MmToPx * _zoom);

    private (double xMm, double yMm) CanvasToWorld(Point canvasPoint) =>
        ((canvasPoint.X - _panOffset.X) / (MmToPx * _zoom),
         (canvasPoint.Y - _panOffset.Y) / (MmToPx * _zoom));

    private static void SetPortsVisible(PanelVisual visual, bool visible)
    {
        // Port dots live on the high-Z overlay layer; only +/- labels toggle here.
        visual.PositivePort.Visibility = Visibility.Collapsed;
        visual.NegativePort.Visibility = Visibility.Collapsed;
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        visual.PositiveLabel.Visibility = v;
        visual.NegativeLabel.Visibility = v;
    }

    private void AddBoviet_Click(object sender, RoutedEventArgs e) =>
        AddPanel(SolarPanelDefinition.CreateBoviet270().Id);

    private void AddGeneric400_Click(object sender, RoutedEventArgs e) =>
        AddPanel(SolarPanelDefinition.CreateGeneric400().Id);

    private void AddGeneric550_Click(object sender, RoutedEventArgs e) =>
        AddPanel(SolarPanelDefinition.CreateGeneric550().Id);

    private void AddCustom_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomPanelDialog { Owner = this };
        if (dialog.ShowDialog() != true || dialog.CreatedDefinition is null) return;
        _project.EnsureDefinition(dialog.CreatedDefinition);
        AddPanel(dialog.CreatedDefinition.Id);
    }

    private void AddPanel(Guid definitionId)
    {
        var count = _project.Graph.Panels.Count;
        var def = _project.RequireDefinition(definitionId);
        // World origin (0,0); stagger along +X so stacked adds don't fully overlap.
        var x = count * (def.WidthMm + PanelGapXMm);
        var panel = _project.AddPanelFromDefinition(definitionId, x, 0);
        FocusWorldMm(panel.PositionXMm + def.WidthMm / 2, panel.PositionYMm + def.HeightMm / 2);
        // Keep Panels/Add side panel open so you can place several modules in a row.
        SetSelection(panels: new[] { panel.Id });
    }

    /// <summary>Default spawn for equipment: world origin, staggered along +X.</summary>
    private (double xMm, double yMm) NextEquipmentPlaceMm(double widthMm = 800)
    {
        var count = _project.Graph.Equipment.Count;
        return (count * (widthMm + 400), 0);
    }

    /// <summary>Pan so a world point sits near the center of the canvas viewport.</summary>
    private void FocusWorldMm(double xMm, double yMm)
    {
        var host = DesignCanvas.Parent as FrameworkElement;
        var vw = host?.ActualWidth > 40 ? host.ActualWidth : 800;
        var vh = host?.ActualHeight > 40 ? host.ActualHeight : 600;
        _panOffset = new Point(
            vw / 2 - xMm * MmToPx * _zoom,
            vh / 2 - yMm * MmToPx * _zoom);
    }

    private void PlaceAndSelectEquipment(ElectricalEquipmentInstance eq)
    {
        FocusWorldMm(eq.PositionXMm + eq.WidthMm / 2, eq.PositionYMm + eq.HeightMm / 2);
        _selectedPanelIds.Clear();
        _selectedConnectionIds.Clear();
        _selectedEquipmentIds.Clear();
        _selectedEquipmentIds.Add(eq.Id);
        // Keep Add side panel open for placing multiple pieces of equipment.
        RefreshAll();
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelection();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!_project.History.CanUndo) return;
        _project.History.Undo();

        // Stay in draw mode if the active roof is still an open polygon.
        if (GetActiveRoofSurface() is { IsClosed: false, Vertices.Count: > 0 })
            _tool = CanvasTool.DrawRoof;
        else if (GetActiveRoofSurface() is { IsClosed: false, Vertices.Count: 0 }
                 && _tool == CanvasTool.DrawRoof)
        {
            // Still drawing; first point undone — keep draw tool active.
        }

        ClearRoofLiveMeasure();
        RefreshAll();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!_project.History.CanRedo) return;
        _project.History.Redo();
        if (GetActiveRoofSurface() is { IsClosed: false })
            _tool = CanvasTool.DrawRoof;
        else if (GetActiveRoofSurface() is { IsClosed: true })
            _tool = CanvasTool.Select;
        ClearRoofLiveMeasure();
        RefreshAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_project.FilePath))
            {
                SolarProjectSerializer.SaveToFile(_project, _project.FilePath);
                RecentProjectsStore.Remember(_project.FilePath);
                RefreshStatusAndInspector();
                StatusText.Text = $"Saved  |  {System.IO.Path.GetFileName(_project.FilePath)}";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "solarSim Project (*.solarproj)|*.solarproj",
                FileName = "Untitled.solarproj",
                AddExtension = true,
                DefaultExt = ".solarproj",
            };
            if (dialog.ShowDialog() != true) return;
            SolarProjectSerializer.SaveToFile(_project, dialog.FileName);
            RecentProjectsStore.Remember(dialog.FileName);
            RefreshStatusAndInspector();
            StatusText.Text = $"Saved  |  {System.IO.Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "solarSim Project (*.solarproj)|*.solarproj",
            FileName = string.IsNullOrEmpty(_project.FilePath)
                ? "Untitled.solarproj"
                : System.IO.Path.GetFileName(_project.FilePath),
            AddExtension = true,
            DefaultExt = ".solarproj",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SolarProjectSerializer.SaveToFile(_project, dialog.FileName);
            RecentProjectsStore.Remember(dialog.FileName);
            RefreshStatusAndInspector();
            StatusText.Text = $"Saved  |  {System.IO.Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            PerformAutoSave(force: true);
        }
        catch
        {
            // don't block close
        }

        var explicitApply = _applyUpdateOnCloseRequested;
        var shouldApply = explicitApply
                          || (AppUpdateService.Instance.ApplyOnExit
                              && AppUpdateService.Instance.DownloadComplete
                              && AppUpdateService.Instance.Available is not null
                              && AppUpdateService.Instance.HasStagedUpdate());
        if (!shouldApply) return;

        try
        {
            if (AppUpdateService.Instance.TryLaunchApplyAndExit(Environment.ProcessId))
                return;

            if (explicitApply)
            {
                e.Cancel = true;
                _applyUpdateOnCloseRequested = false;
                MessageBox.Show(this,
                    "Couldn't start the update installer.\n\n" +
                    "The download may be missing — open Settings → Check for updates, then try again.",
                    "Update failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            if (explicitApply)
            {
                e.Cancel = true;
                _applyUpdateOnCloseRequested = false;
                MessageBox.Show(this, ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ScheduleAutoSave()
    {
        if (!_autoSaveEnabled) return;
        if (string.IsNullOrWhiteSpace(_project.FilePath)) return;
        _autoSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _autoSaveTimer.Stop();
        _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        _autoSaveTimer.Start();
    }

    private void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        _autoSaveTimer?.Stop();
        PerformAutoSave(force: false);
    }

    private void PerformAutoSave(bool force)
    {
        if (!_autoSaveEnabled && !force) return;
        if (string.IsNullOrWhiteSpace(_project.FilePath)) return;
        try
        {
            var path = _project.FilePath;
            SolarProjectSerializer.SaveToFile(_project, path);
            _lastAutoSaveError = null;
            UpdateProjectNameChrome($"Autosaved on this PC · {path}");
        }
        catch (Exception ex)
        {
            _lastAutoSaveError = ex.Message;
            if (StatusText is not null)
                StatusText.Text = $"Autosave failed  |  {ex.Message}";
        }
    }

    private void UpdateProjectNameChrome(string? tooltipOverride = null)
    {
        if (FileMenuButton is null) return;
        var name = FriendlyProjectName();
        FileMenuButton.Content = name;
        FileMenuButton.ToolTip = tooltipOverride
            ?? (string.IsNullOrEmpty(_project.FilePath)
                ? "Project menu"
                : $"Project menu · {_project.FilePath}");
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "solarSim Project (*.solarproj)|*.solarproj",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var loaded = SolarProjectSerializer.LoadFromFile(dialog.FileName);
            ReplaceProject(loaded);
            RecentProjectsStore.Remember(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to open project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReplaceProject(SolarProject loaded)
    {
        foreach (var visual in _panelVisuals.Values)
            DesignCanvas.Children.Remove(visual.Root);
        foreach (var visual in _equipmentVisuals.Values)
            DesignCanvas.Children.Remove(visual.Root);
        foreach (var wire in _wireVisuals.Values)
            wire.RemoveFrom(DesignCanvas);
        ClearPanelPortHitOverlays();
        _panelVisuals.Clear();
        _equipmentVisuals.Clear();
        _wireVisuals.Clear();

        // Copy loaded state into current project by reconstructing through serializer round-trip fields.
        // Simplest: swap by re-reading into UI-bound instance via field replacement pattern.
        // We'll re-assign by clearing and importing.
        _project.Graph.Clear();
        _project.Definitions.Clear();
        foreach (var def in loaded.Definitions.Values)
            _project.Definitions[def.Id] = def;
        foreach (var builtIn in SolarPanelDefinition.BuiltInLibrary)
            _project.EnsureDefinition(builtIn);

        foreach (var panel in loaded.Graph.Panels.Values)
            _project.Graph.AddPanel(panel);

        foreach (var equipment in loaded.Graph.Equipment.Values)
            _project.Graph.AddEquipment(equipment);

        foreach (var connection in loaded.Graph.Connections.Values)
        {
            _project.Graph.TryConnect(connection.StartPortId, connection.EndPortId, connection.Wire.Clone(), out _);
        }

        _project.Roofs.Clear();
        foreach (var roof in loaded.Roofs.Roofs)
        {
            var copy = CloneRoofSurface(roof);
            _project.Roofs.AddExisting(copy, makeActive: false);
        }

        if (loaded.Roofs.ActiveRoofId is Guid activeId && _project.Roofs.Find(activeId) is not null)
            _project.Roofs.SetActive(activeId);
        else if (_project.Roofs.Roofs.Count > 0)
            _project.Roofs.SetActive(_project.Roofs.Roofs[0].Id);

        _project.Units.PreferredLengthUnit = loaded.Units.PreferredLengthUnit;
        SelectCurrentUnitInCombo();

        _project.Name = loaded.Name;
        _project.FilePath = loaded.FilePath;
        _project.ProjectId = loaded.ProjectId;
        _project.History.Clear();
        ClearTransientSelection();
        RefreshAll();
    }

    private void StringsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = StringsList.SelectedItem as StringListItem
                   ?? (StringsList.SelectedItem as ListBoxItem)?.Content as StringListItem;
        if (item is null) return;
        var pvString = _project.Graph.Strings.FirstOrDefault(s => s.Id == item.StringId);
        if (pvString is null) return;
        SetSelection(panels: pvString.PanelIdsInSeriesOrder);
    }

    private int IndexOfStringId(Guid stringId)
    {
        for (var i = 0; i < _project.Graph.Strings.Count; i++)
        {
            if (_project.Graph.Strings[i].Id == stringId)
                return i;
        }
        return 0;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Delete always targets canvas selection (even if setback box still has focus).
        if (e.Key is Key.Delete)
        {
            if (DeleteSelection())
                e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase) return;

        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Undo_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Redo_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Redo_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetSelection(
                panels: _project.Graph.Panels.Keys,
                connections: _project.Graph.Connections.Keys);
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            DuplicateSelectedPanels();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelectedPanels();
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PastePanelsNearViewCenter();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            if (DeleteSelection())
                e.Handled = true;
        }
        else if (e.Key == Key.R)
        {
            var rotated = false;
            if (_selectedPanelIds.Count > 0)
            {
                foreach (var rotateId in _selectedPanelIds.ToList())
                {
                    var panel = _project.Graph.GetPanel(rotateId);
                    _project.History.Execute(new RotatePanelCommand(
                        _project, rotateId, panel.RotationDegrees, panel.RotationDegrees + 90));
                }
                rotated = true;
            }

            if (_selectedEquipmentIds.Count > 0)
            {
                // Shift+R = 90°, plain R = 15° (Canva-like nudge)
                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 90.0 : 15.0;
                foreach (var id in _selectedEquipmentIds.ToList())
                {
                    if (!_project.Graph.TryGetEquipment(id, out var eq)) continue;
                    eq.RotateBy(step);
                    rotated = true;
                }

                if (rotated)
                    _project.NotifyChanged("Rotate equipment");
            }

            if (rotated)
            {
                RefreshAll();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter && _tool == CanvasTool.DrawRoof)
        {
            if (GetActiveRoofSurface() is { Vertices.Count: >= 3 } enterRoof)
            {
                _project.History.Execute(new CloseRoofCommand(_project, enterRoof.Id));
                if (enterRoof.IsClosed)
                {
                    _tool = CanvasTool.Select;
                    ClearRoofLiveMeasure();
                    RefreshAll();
                }
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelWireDrag();
            EndMarquee(commit: false);
            if (_tool == CanvasTool.Measure)
            {
                ClearMeasureTool();
                StatusText.Text = "MEASURE  ·  Cleared — click to start again";
                e.Handled = true;
                return;
            }
            // Leave in-progress roof vertices — use Ctrl+Z to step back segments.
            if (_tool == CanvasTool.DrawRoof)
            {
                ClearRoofLiveMeasure();
                _tool = CanvasTool.Select;
                StatusText.Text = GetActiveRoofSurface() is { IsClosed: false, Vertices.Count: > 0 }
                    ? "Roof draw paused — open outline kept. Draw Roof again to continue, or Ctrl+Z to remove segments."
                    : StatusText.Text;
                RefreshAll();
                e.Handled = true;
                return;
            }
            ClearRoofLiveMeasure();
            _tool = CanvasTool.Select;
            ClearTransientSelection();
            RefreshAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && _tool == CanvasTool.DrawRoof)
        {
            // Backspace = undo last roof segment (same as Ctrl+Z while drawing).
            if (_project.History.CanUndo)
            {
                Undo_Click(sender, e);
                e.Handled = true;
            }
        }
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mouse = e.GetPosition(DesignCanvas);
        var (beforeX, beforeY) = CanvasToWorld(mouse);
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        _zoom = Math.Clamp(_zoom * factor, 0.25, 4.0);
        var after = WorldToCanvas(beforeX, beforeY);
        _panOffset.X += mouse.X - after.x;
        _panOffset.Y += mouse.Y - after.y;
        RefreshAll();
        e.Handled = true;
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        DesignCanvas.Focus();
        var pos = e.GetPosition(DesignCanvas);

        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
        {
            _isPanning = true;
            _panStart = pos;
            _panOrigin = _panOffset;
            DesignCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        if (_tool == CanvasTool.DrawRoof)
        {
            var active = _project.Roofs.EnsureActiveRoof();
            var point = ResolveRoofDrawPoint(active, pos, out var closing, out _);
            if (closing && active.Vertices.Count >= 3)
            {
                _project.History.Execute(new CloseRoofCommand(_project, active.Id));
                if (active.IsClosed)
                {
                    _tool = CanvasTool.Select;
                    ClearRoofLiveMeasure();
                    RefreshAll();
                }
                e.Handled = true;
                return;
            }

            _project.History.Execute(new AddRoofVertexCommand(_project, active.Id, point));
            ClearRoofLiveMeasure();
            RefreshAll();
            e.Handled = true;
            return;
        }

        if (_tool == CanvasTool.Measure)
        {
            var (xMm, yMm) = CanvasToWorld(pos);
            _measurePoints.Add(new Point2Mm(xMm, yMm));
            RebuildMeasureVisuals();
            e.Handled = true;
            return;
        }

        if (_tool == CanvasTool.PlaceObstacle)
        {
            var active = _project.Roofs.EnsureActiveRoof();
            var (xMm, yMm) = CanvasToWorld(pos);
            const double w = 600;
            const double h = 600;
            _project.Roofs.EnsureActiveRoof().AddObstacle(new RoofObstacle(
                Guid.NewGuid(),
                RoofObstacleKind.Vent,
                xMm - w / 2,
                yMm - h / 2,
                w,
                h,
                "Vent"));
            _tool = CanvasTool.Select;
            _project.NotifyChanged("Add obstacle");
            RefreshAll();
            e.Handled = true;
            return;
        }

        // Drag roof vertex (unlocked only)
        if (FindRoofVertexAt(pos) is int vertexIndex
            && GetActiveRoofSurface() is { IsLocked: false })
        {
            _draggingRoofVertexIndex = vertexIndex;
            DesignCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (FindObstacleAt(pos) is Guid obstacleId)
        {
            _selectedObstacleId = obstacleId;
            _selectedPanelIds.Clear();
            _selectedConnectionIds.Clear();
            _selectedEquipmentIds.Clear();
            RefreshAll();
            e.Handled = true;
            return;
        }

        if (FindEquipmentAt(pos) is Guid equipmentId)
        {
            BeginEquipmentInteraction(equipmentId, pos, additive: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
            return;
        }

        if (FindPanelAt(pos) is Guid panelId)
        {
            var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                           || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (additive)
            {
                if (_selectedPanelIds.Contains(panelId))
                    _selectedPanelIds.Remove(panelId);
                else
                    _selectedPanelIds.Add(panelId);
                _selectedConnectionIds.Clear();
                RefreshAll();
            }
            else if (!_selectedPanelIds.Contains(panelId))
            {
                SetSelection(panels: new[] { panelId });
            }
            else
            {
                RefreshStatusAndInspector();
            }

            // Drag all currently selected panels together if the clicked one is selected
            _draggingPanelId = panelId;
            _dragStartMouse = pos;
            _dragOrigins.Clear();
            var dragIds = _selectedPanelIds.Contains(panelId)
                ? _selectedPanelIds.ToList()
                : new List<Guid> { panelId };
            foreach (var id in dragIds)
            {
                var panel = _project.Graph.GetPanel(id);
                _dragOrigins[id] = (panel.PositionXMm, panel.PositionYMm);
            }
            _dragMoved = false;
            DesignCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Move unlocked roof only with Alt+drag — plain drag over the house starts marquee
        // so panel box-select / wiring aren't stolen by the outline.
        if (_tool == CanvasTool.Select
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            && FindClosedRoofAt(pos) is Guid roofId
            && _project.Roofs.Find(roofId) is { IsLocked: false })
        {
            _project.Roofs.SetActive(roofId);
            BeginRoofBodyDrag(pos);
            e.Handled = true;
            return;
        }

        // Empty canvas (or roof fill) → start marquee highlight
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            ClearTransientSelection();

        BeginMarquee(pos);
        DesignCanvas.CaptureMouse();
        RefreshAll();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(DesignCanvas);

        if (_isPanning &&
            (e.MiddleButton == MouseButtonState.Pressed ||
             (e.LeftButton == MouseButtonState.Pressed && Keyboard.IsKeyDown(Key.Space))))
        {
            _panOffset = new Point(
                _panOrigin.X + (pos.X - _panStart.X),
                _panOrigin.Y + (pos.Y - _panStart.Y));
            RefreshAll();
            return;
        }

        if (_rotatingEquipmentId is Guid rotatingId && e.LeftButton == MouseButtonState.Pressed)
        {
            if (_project.Graph.TryGetEquipment(rotatingId, out var eq)
                && _equipmentVisuals.TryGetValue(rotatingId, out var visual))
            {
                var center = GetEquipmentCanvasCenter(visual);
                var mouseAngle = Math.Atan2(pos.Y - center.Y, pos.X - center.X) * (180.0 / Math.PI);
                var delta = mouseAngle - _rotateStartMouseAngleDeg;
                var next = _rotateStartEquipmentDeg + delta;

                // Default: snap to 0 / 90 / 180 / 270. Shift = 15°, Alt = free.
                if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                {
                    // free
                }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    next = Math.Round(next / 15.0) * 15.0;
                }
                else
                {
                    next = Math.Round(next / 90.0) * 90.0;
                }

                eq.SetRotation(next);
                _rotateMoved = true;
                UpdateEquipmentVisual(visual, eq);
                UpdateRotateDegreeLabel(center, eq.RotationDegrees);
                RebuildWireVisuals();
                RefreshStatusAndInspector();
            }
            return;
        }

        if (_rotatingRoof && e.LeftButton == MouseButtonState.Pressed)
        {
            var (cx, cy) = WorldToCanvas(_roofRotatePivot.X, _roofRotatePivot.Y);
            var mouseAngle = Math.Atan2(pos.Y - cy, pos.X - cx) * (180.0 / Math.PI);
            var delta = SnapRoofDragDegrees(mouseAngle - _roofRotateStartMouseDeg);

            if (Math.Abs(delta - _roofRotateLiveDegrees) > 0.05)
            {
                _rotateMoved = true;
                ApplyLiveRoofRotation(delta);
            }
            return;
        }

        if (_draggingRoofBody && e.LeftButton == MouseButtonState.Pressed)
        {
            var (x0, y0) = CanvasToWorld(_roofDragStartCanvas);
            var (x1, y1) = CanvasToWorld(pos);
            var dx = x1 - x0;
            var dy = y1 - y0;
            if (Math.Abs(dx - _roofDragDxMm) > 0.1 || Math.Abs(dy - _roofDragDyMm) > 0.1)
            {
                _rotateMoved = true; // reuse moved flag for commit
                ApplyLiveRoofTranslate(dx, dy);
            }
            return;
        }

        if (_draggingWaypointConnectionId is Guid wpConnId
            && _draggingWaypointIndex is int wpIdx
            && e.LeftButton == MouseButtonState.Pressed
            && _project.Graph.Connections.TryGetValue(wpConnId, out var routed)
            && wpIdx >= 0
            && wpIdx < routed.Wire.Waypoints.Count)
        {
            var (xMm, yMm) = CanvasToWorld(pos);
            // Ortho snap relative to previous/next point (Alt = free).
            if (!Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt))
            {
                Point2Mm anchor;
                if (wpIdx > 0)
                    anchor = routed.Wire.Waypoints[wpIdx - 1];
                else if (_project.Graph.TryGetPort(routed.StartPortId, out var sp))
                {
                    var c = CanvasToWorld(GetPortCanvasPoint(sp));
                    anchor = new Point2Mm(c.xMm, c.yMm);
                }
                else
                    anchor = new Point2Mm(xMm, yMm);

                var snapped = RoofGeometry.SnapOrthogonal(anchor, new Point2Mm(xMm, yMm));
                xMm = snapped.X;
                yMm = snapped.Y;
            }

            routed.Wire.Waypoints[wpIdx] = new Point2Mm(xMm, yMm);
            RebuildWireVisuals();
            RefreshStatusAndInspector();
            return;
        }

        if (_draggingWireSegmentConnectionId is Guid segConnId
            && _draggingWireSegmentIndex >= 0
            && e.LeftButton == MouseButtonState.Pressed
            && _project.Graph.Connections.TryGetValue(segConnId, out var segWire)
            && _project.Graph.TryGetPort(segWire.StartPortId, out var segStart)
            && _project.Graph.TryGetPort(segWire.EndPortId, out var segEnd))
        {
            var (xMm, yMm) = CanvasToWorld(pos);
            var points = BuildWireWorldPolyline(segWire, segStart, segEnd);
            var i = _draggingWireSegmentIndex;
            if (i < 0 || i >= points.Count - 1) return;

            // Path index → waypoint index is pathIndex - 1 (ports are not waypoints).
            void MovePathPoint(int pathIndex, double nx, double ny)
            {
                var wpIndex = pathIndex - 1;
                if (wpIndex < 0 || wpIndex >= segWire.Wire.Waypoints.Count) return;
                segWire.Wire.Waypoints[wpIndex] = new Point2Mm(nx, ny);
            }

            if (_draggingWireSegmentHorizontal)
            {
                MovePathPoint(i, points[i].X, yMm);
                MovePathPoint(i + 1, points[i + 1].X, yMm);
            }
            else
            {
                MovePathPoint(i, xMm, points[i].Y);
                MovePathPoint(i + 1, xMm, points[i + 1].Y);
            }

            RebuildWireVisuals();
            RefreshStatusAndInspector();
            return;
        }

        if (_draggingRoofVertexIndex is int vIndex && e.LeftButton == MouseButtonState.Pressed
            && GetActiveRoofSurface() is { } dragRoof)
        {
            var (xMm, yMm) = CanvasToWorld(pos);
            var raw = new Point2Mm(xMm, yMm);
            var free = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
            var axisTolMm = RoofAxisSnapPx / (MmToPx * Math.Max(_zoom, 0.05));
            var snapped = RoofGeometry.SnapEditVertex(
                vIndex,
                raw,
                dragRoof.Vertices,
                axisTolMm,
                free,
                out var alignX,
                out var alignY);
            _roofAlignXSource = alignX;
            _roofAlignYSource = alignY;
            dragRoof.MoveVertex(vIndex, snapped);
            RefreshAll();
            UpdateRoofEditSnapGuides(dragRoof, vIndex, snapped);
            return;
        }

        if (_tool == CanvasTool.DrawRoof && GetActiveRoofSurface() is { Vertices.Count: > 0 } measureRoof)
        {
            UpdateRoofLiveMeasure(pos, measureRoof);
            return;
        }

        if (_tool == CanvasTool.Measure && _measurePoints.Count > 0)
        {
            UpdateMeasureRubberBand(pos);
            return;
        }

        ClearRoofLiveMeasure();
        ClearMeasureRubberBand();

        if (_isMarqueeSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateMarquee(pos);
            return;
        }

        if (_wireFromPortId is Guid fromPortId)
        {
            EnsurePreviewWire();
            var fromPort = _project.Graph.GetPort(fromPortId);
            var start = GetPortCanvasPoint(fromPort);
            var n1 = PortExitNormalCanvas(fromPort);

            Point endPt;
            Vector n2;
            var target = FindCompatiblePortNear(pos, fromPortId);
            if (target is not null)
            {
                endPt = GetPortCanvasPoint(target);
                n2 = PortExitNormalCanvas(target);
                _previewWire!.StrokeThickness = 3.5;
                _previewWire.StrokeDashArray = null;
                ShowHoverPort(endPt, target.Polarity);
                UpdatePreviewPlugHint(fromPort.Polarity, target.Polarity, endPt);
            }
            else
            {
                endPt = pos;
                n2 = new Vector(0, 1);
                _previewWire!.StrokeThickness = 2.5;
                _previewWire.StrokeDashArray = new DoubleCollection { 5, 3 };
                HideHoverPort();
                ClearPreviewPlugHint();
            }

            _previewWire.Stroke = PolarityBrush(fromPort.Polarity);
            var ortho = PvWireRouting.OrthoPreview(
                new PvVec2(start.X, start.Y),
                new PvVec2(n1.X, n1.Y),
                new PvVec2(endPt.X, endPt.Y),
                new PvVec2(n2.X, n2.Y));
            _previewWire.Data = BuildRoundedOrthoGeometry(ortho, cornerRadius: Math.Clamp(6 * _zoom, 4, 10));
            return;
        }

        if (_draggingPanelId is not null && e.LeftButton == MouseButtonState.Pressed && _dragOrigins.Count > 0)
        {
            var dxPx = pos.X - _dragStartMouse.X;
            var dyPx = pos.Y - _dragStartMouse.Y;
            if (Math.Abs(dxPx) + Math.Abs(dyPx) > 2) _dragMoved = true;

            var dxMm = dxPx / (MmToPx * _zoom);
            var dyMm = dyPx / (MmToPx * _zoom);

            var primaryOrigin = _dragOrigins[_draggingPanelId.Value];
            var proposedX = primaryOrigin.x + dxMm;
            var proposedY = primaryOrigin.y + dyMm;

            var isEquipment = _project.Graph.Equipment.ContainsKey(_draggingPanelId.Value);
            if (!isEquipment)
            {
                var primary = _project.Graph.GetPanel(_draggingPanelId.Value);
                var allowSnap = !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt);
                (proposedX, proposedY) = ResolvePanelDragPosition(
                    primary, primaryOrigin.x, primaryOrigin.y, proposedX, proposedY, allowSnap);
            }

            var snapDx = proposedX - primaryOrigin.x;
            var snapDy = proposedY - primaryOrigin.y;

            foreach (var (id, origin) in _dragOrigins)
            {
                var nx = origin.x + snapDx;
                var ny = origin.y + snapDy;

                if (_project.Graph.TryGetEquipment(id, out var equipment))
                {
                    equipment.SetPosition(nx, ny);
                    continue;
                }

                if (!_project.Graph.TryGetPanel(id, out var panel)) continue;
                // Free drag on the map — setback/roof validity is advisory (HUD), not a hard stop.
                panel.SetPosition(nx, ny);
            }

            RefreshAll();
            if (!isEquipment)
                UpdatePanelAlignmentGuides(_draggingPanelId.Value, _dragOrigins.Keys.ToHashSet());
            else
                ClearAlignmentGuides();
        }
    }

    /// <summary>
    /// Prefer edge snap when Alt is not held. Setback / roof bounds do not block free drag.
    /// </summary>
    private (double x, double y) ResolvePanelDragPosition(
        SolarPanelInstance panel,
        double originX,
        double originY,
        double rawX,
        double rawY,
        bool allowSnap)
    {
        if (!allowSnap)
            return (rawX, rawY);

        var (snappedX, snappedY) = ApplyPanelSnap(panel.Id, rawX, rawY);
        return (snappedX, snappedY);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            DesignCanvas.ReleaseMouseCapture();
            return;
        }

        if (_rotatingEquipmentId is not null)
        {
            if (_rotateMoved)
                _project.NotifyChanged("Rotate equipment");
            _rotatingEquipmentId = null;
            _rotateMoved = false;
            ClearRotateDegreeLabel();
            DesignCanvas.ReleaseMouseCapture();
            RefreshAll();
            return;
        }

        if (_rotatingRoof)
        {
            var deg = _roofRotateLiveDegrees;
            // Restore baseline, then commit via undoable command (avoids double-apply).
            foreach (var (id, before) in _roofRotateBaseline)
            {
                var roof = _project.Roofs.Find(id);
                if (roof is null) continue;
                roof.SetVertices(before, closed: true);
            }

            _rotatingRoof = false;
            _roofRotateBaseline.Clear();
            ClearRotateDegreeLabel();
            DesignCanvas.ReleaseMouseCapture();

            if (_rotateMoved && Math.Abs(deg) >= 0.05)
            {
                _project.History.Execute(new RotateRoofsCommand(_project, deg));
                StatusText.Text = $"ROOF  |  Rotated {deg:0.#}°";
            }

            _rotateMoved = false;
            RefreshAll();
            return;
        }

        if (_draggingRoofBody)
        {
            var dx = _roofDragDxMm;
            var dy = _roofDragDyMm;
            foreach (var (id, before) in _roofRotateBaseline)
            {
                var roof = _project.Roofs.Find(id);
                if (roof is null) continue;
                roof.SetVertices(before, closed: true);
            }

            _draggingRoofBody = false;
            _roofRotateBaseline.Clear();
            DesignCanvas.ReleaseMouseCapture();

            if (_rotateMoved && (Math.Abs(dx) >= 0.5 || Math.Abs(dy) >= 0.5))
            {
                _project.History.Execute(new TranslateRoofsCommand(_project, dx, dy));
                StatusText.Text = "ROOF  |  Moved";
            }

            _rotateMoved = false;
            _roofDragDxMm = 0;
            _roofDragDyMm = 0;
            RefreshAll();
            return;
        }

        if (_draggingWaypointConnectionId is not null)
        {
            _project.NotifyChanged("Move wire waypoint");
            _draggingWaypointConnectionId = null;
            _draggingWaypointIndex = null;
            DesignCanvas.ReleaseMouseCapture();
            RefreshAll();
            return;
        }

        if (_draggingWireSegmentConnectionId is not null)
        {
            _project.NotifyChanged("Move wire segment");
            _draggingWireSegmentConnectionId = null;
            _draggingWireSegmentIndex = -1;
            DesignCanvas.ReleaseMouseCapture();
            RefreshAll();
            return;
        }

        if (_draggingRoofVertexIndex is not null)
        {
            _draggingRoofVertexIndex = null;
            ClearRoofLiveMeasure();
            _project.NotifyChanged("Move roof vertex");
            DesignCanvas.ReleaseMouseCapture();
            RefreshAll();
            return;
        }

        if (_isMarqueeSelecting)
        {
            EndMarquee(commit: true);
            DesignCanvas.ReleaseMouseCapture();
            RefreshAll();
            return;
        }

        if (_wireFromPortId is Guid fromPortId)
        {
            var pos = e.GetPosition(DesignCanvas);
            var target = FindCompatiblePortNear(pos, fromPortId);
            if (target is not null)
            {
                try
                {
                    var fromPort = _project.Graph.GetPort(fromPortId);
                    var cmd = new ConnectPortsCommand(_project, fromPortId, target.Id);
                    _project.History.Execute(cmd);
                    ShowConnectedToast(
                        GetPortCanvasPoint(target),
                        fromPort.Polarity,
                        target.Polarity);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Unable to connect", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            CancelWireDrag();
            RefreshAll();
            DesignCanvas.ReleaseMouseCapture();
            return;
        }

        if (_draggingPanelId is not null)
        {
            DesignCanvas.ReleaseMouseCapture();
            if (_dragMoved)
            {
                foreach (var (id, origin) in _dragOrigins.ToList())
                {
                    if (_project.Graph.TryGetEquipment(id, out var equipment))
                    {
                        var finalX = equipment.PositionXMm;
                        var finalY = equipment.PositionYMm;
                        equipment.SetPosition(origin.x, origin.y);
                        equipment.SetPosition(finalX, finalY);
                        _project.NotifyChanged("Moved equipment");
                        continue;
                    }

                    if (!_project.Graph.TryGetPanel(id, out var panel)) continue;
                    var fx = panel.PositionXMm;
                    var fy = panel.PositionYMm;
                    if (Math.Abs(fx - origin.x) > 0.01 || Math.Abs(fy - origin.y) > 0.01)
                    {
                        panel.SetPosition(origin.x, origin.y);
                        _project.History.Execute(new MovePanelCommand(
                            _project, id, origin.x, origin.y, fx, fy));
                    }
                }
            }

            _draggingPanelId = null;
            _dragOrigins.Clear();
            ClearAlignmentGuides();
            RefreshAll();
        }
    }

    private void Canvas_MouseLeave(object sender, MouseEventArgs e)
    {
        // Keep drag if captured
    }

    private void Port_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        ElectricalPort? port = null;

        if (element.Tag is Guid overlayPortId)
        {
            port = _project.Graph.GetPort(overlayPortId);
        }
        else if (element is Ellipse ellipse)
        {
            var panelVisual = _panelVisuals.Values.FirstOrDefault(v =>
                ReferenceEquals(v.PositivePort, ellipse) || ReferenceEquals(v.NegativePort, ellipse));
            if (panelVisual is not null)
            {
                var panel = _project.Graph.GetPanel(panelVisual.InstanceId);
                port = ReferenceEquals(panelVisual.PositivePort, ellipse) ? panel.PositivePort : panel.NegativePort;
            }
            else
            {
                foreach (var eqVisual in _equipmentVisuals.Values)
                {
                    foreach (var (portId, portEllipse) in eqVisual.PortEllipses)
                    {
                        if (!ReferenceEquals(portEllipse, ellipse)) continue;
                        port = _project.Graph.GetPort(portId);
                        break;
                    }
                    if (port is not null) break;
                }
            }
        }

        if (port is null) return;

        if (port.IsOccupied)
        {
            MessageBox.Show(this, "This terminal is already connected.", "Port occupied",
                MessageBoxButton.OK, MessageBoxImage.Information);
            e.Handled = true;
            return;
        }

        _wireFromPortId = port.Id;
        foreach (var v in _panelVisuals.Values) SetPortsVisible(v, true);
        foreach (var v in _equipmentVisuals.Values) SetEquipmentPortsVisible(v, true);
        EnsurePreviewWire();
        DesignCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void EnsurePreviewWire()
    {
        if (_previewWire is not null) return;
        _previewWire = new System.Windows.Shapes.Path
        {
            Stroke = (Brush)FindResource("PositiveBrush"),
            StrokeThickness = 2.5,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        DesignCanvas.Children.Add(_previewWire);
        Panel.SetZIndex(_previewWire, 1600);
    }

    private void CancelWireDrag()
    {
        _wireFromPortId = null;
        if (_previewWire is not null)
        {
            DesignCanvas.Children.Remove(_previewWire);
            _previewWire = null;
        }
        ClearPreviewPlugHint();
        HideHoverPort();
        foreach (var visual in _panelVisuals.Values)
        {
            if (!_selectedPanelIds.Contains(visual.InstanceId))
                SetPortsVisible(visual, false);
        }
        foreach (var visual in _equipmentVisuals.Values)
        {
            if (!_selectedEquipmentIds.Contains(visual.InstanceId))
                SetEquipmentPortsVisible(visual, false);
        }
    }

    private ElectricalPort? FindCompatiblePortNear(Point canvasPoint, Guid fromPortId)
    {
        var from = _project.Graph.GetPort(fromPortId);
        ElectricalPort? best = null;
        var bestDist = PortHitRadiusPx * 2.5;

        IElectricalComponent? fromOwner = null;
        _project.Graph.TryGetComponent(from.OwnerComponentId, out fromOwner);
        if (fromOwner is null) return null;

        foreach (var panel in _project.Graph.Panels.Values)
        {
            if (!ShowsPanels) break;
            foreach (var port in panel.Ports)
            {
                if (port.Id == fromPortId) continue;
                if (port.OwnerComponentId == from.OwnerComponentId) continue;
                if (port.IsOccupied) continue;

                var validation = ConnectionValidator.ValidateDcConnection(from, port, fromOwner, panel);
                if (!validation.IsValid) continue;

                var p = GetPortCanvasPoint(port);
                var dist = Hypot(p.X - canvasPoint.X, p.Y - canvasPoint.Y);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = port;
                }
            }
        }

        if (ShowsEquipment)
        {
            foreach (var equipment in _project.Graph.Equipment.Values)
            {
                foreach (var port in equipment.Ports)
                {
                    if (port.Id == fromPortId) continue;
                    if (port.IsOccupied) continue;

                    var validation = ConnectionValidator.ValidateDcConnection(from, port, fromOwner, equipment);
                    if (!validation.IsValid) continue;

                    var p = GetPortCanvasPoint(port);
                    var dist = Hypot(p.X - canvasPoint.X, p.Y - canvasPoint.Y);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = port;
                    }
                }
            }
        }

        return best;
    }

    private void ShowHoverPort(Point canvasPoint, Polarity polarity)
    {
        if (_hoverPortMarker is null)
        {
            _hoverPortMarker = new Ellipse
            {
                Width = 26,
                Height = 26,
                StrokeThickness = 2.5,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_hoverPortMarker);
            Panel.SetZIndex(_hoverPortMarker, 1700);
        }

        _hoverPortMarker.Stroke = PolarityBrush(polarity);
        Canvas.SetLeft(_hoverPortMarker, canvasPoint.X - 13);
        Canvas.SetTop(_hoverPortMarker, canvasPoint.Y - 13);
        _hoverPortMarker.Visibility = Visibility.Visible;
    }

    private void HideHoverPort()
    {
        if (_hoverPortMarker is not null)
            _hoverPortMarker.Visibility = Visibility.Collapsed;
    }

    private void UpdatePreviewPlugHint(Polarity fromPolarity, Polarity toPolarity, Point near)
    {
        if (_previewPlugHint is null)
        {
            _previewPlugHint = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                Padding = new Thickness(8, 4, 8, 4),
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_previewPlugHint);
            Panel.SetZIndex(_previewPlugHint, 1800);
        }

        var fromLabel = fromPolarity == Polarity.Positive ? "+" : "−";
        var toLabel = toPolarity == Polarity.Positive ? "+" : "−";
        _previewPlugHint.Text = $"PLUG  {fromLabel} → {toLabel}";
        Canvas.SetLeft(_previewPlugHint, near.X + 14);
        Canvas.SetTop(_previewPlugHint, near.Y - 28);
        _previewPlugHint.Visibility = Visibility.Visible;
    }

    private void ClearPreviewPlugHint()
    {
        if (_previewPlugHint is not null)
            _previewPlugHint.Visibility = Visibility.Collapsed;
    }

    private void ShowConnectedToast(Point near, Polarity fromPolarity, Polarity toPolarity)
    {
        if (_connectedToast is not null)
            DesignCanvas.Children.Remove(_connectedToast);

        var fromLabel = fromPolarity == Polarity.Positive ? "+" : "−";
        var toLabel = toPolarity == Polarity.Positive ? "+" : "−";
        _connectedToast = new TextBlock
        {
            Text = $"PLUGGED  {fromLabel} → {toLabel}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_connectedToast, near.X + 12);
        Canvas.SetTop(_connectedToast, near.Y - 18);
        DesignCanvas.Children.Add(_connectedToast);

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_connectedToast is not null)
            {
                DesignCanvas.Children.Remove(_connectedToast);
                _connectedToast = null;
            }
        };
        timer.Start();
    }

    private (double x, double y) ApplyPanelSnap(Guid movingId, double xMm, double yMm)
    {
        var moving = _project.Graph.GetPanel(movingId);
        var movingDef = _project.RequireDefinition(moving.DefinitionId);
        var movingSize = GetLogicalSizeMm(movingDef, moving.RotationDegrees);
        var gapY = PanelGapYMm;

        // Independent axis snaps — top/bottom must catch even when X is slightly off (old hypot score missed that).
        double bestX = xMm;
        double bestY = yMm;
        var bestDx = SnapThresholdMm;
        var bestDy = SnapThresholdMm;

        foreach (var other in _project.Graph.Panels.Values)
        {
            if (other.Id == movingId) continue;
            var otherDef = _project.RequireDefinition(other.DefinitionId);
            var otherSize = GetLogicalSizeMm(otherDef, other.RotationDegrees);

            var ox = other.PositionXMm;
            var oy = other.PositionYMm;
            var ow = otherSize.width;
            var oh = otherSize.height;
            var mw = movingSize.width;
            var mh = movingSize.height;

            // X candidates: left/right neighbor seats + left/right/center edge align
            var xCandidates = new[]
            {
                ox + ow + PanelGapXMm, // right of other
                ox - mw - PanelGapXMm, // left of other
                ox,                   // left edges
                ox + ow - mw,         // right edges
                ox + (ow - mw) / 2,   // centers
            };
            foreach (var cx in xCandidates)
            {
                var dx = Math.Abs(cx - xMm);
                if (dx < bestDx)
                {
                    bestDx = dx;
                    bestX = cx;
                }
            }

            // Y candidates: above/below neighbor seats + top/bottom/center edge align
            var yCandidates = new[]
            {
                oy + oh + gapY, // below other
                oy - mh - gapY, // above other
                oy,             // top edges
                oy + oh - mh,   // bottom edges
                oy + (oh - mh) / 2, // centers
            };
            foreach (var cy in yCandidates)
            {
                var dy = Math.Abs(cy - yMm);
                if (dy < bestDy)
                {
                    bestDy = dy;
                    bestY = cy;
                }
            }
        }

        return (bestX, bestY);
    }

    /// <summary>
    /// Canva-style smart guides: thin magenta dotted lines when edges/centers line up while dragging.
    /// </summary>
    private void UpdatePanelAlignmentGuides(Guid movingId, HashSet<Guid> ignoreIds)
    {
        ClearAlignmentGuides();
        if (!_project.Graph.TryGetPanel(movingId, out var moving)) return;

        var movingDef = _project.RequireDefinition(moving.DefinitionId);
        var movingSize = GetLogicalSizeMm(movingDef, moving.RotationDegrees);
        var ml = moving.PositionXMm;
        var mt = moving.PositionYMm;
        var mr = ml + movingSize.width;
        var mb = mt + movingSize.height;
        var mcx = (ml + mr) * 0.5;
        var mcy = (mt + mb) * 0.5;

        // Tight — only when actually aligned (snap lands exact; free-drag within a few mm still shows).
        const double tolMm = 4.0;
        // Soft overhang past the objects (px → mm) so lines read like Canva without dominating.
        var padMm = 28.0 / (MmToPx * Math.Max(_zoom, 0.2));
        var extendMm = 56.0 / (MmToPx * Math.Max(_zoom, 0.2));

        // Bucket world coords → span on the cross axis.
        var vGuides = new Dictionary<long, (double x, double y0, double y1)>();
        var hGuides = new Dictionary<long, (double y, double x0, double x1)>();

        static long Bucket(double mm) => (long)Math.Round(mm * 10.0); // 0.1 mm

        void AddV(double x, double yA0, double yA1, double yB0, double yB1, bool extendBeyond)
        {
            var y0 = Math.Min(Math.Min(yA0, yA1), Math.Min(yB0, yB1)) - padMm;
            var y1 = Math.Max(Math.Max(yA0, yA1), Math.Max(yB0, yB1)) + padMm;
            if (extendBeyond)
            {
                y0 -= extendMm;
                y1 += extendMm * 0.35;
            }

            var key = Bucket(x);
            if (vGuides.TryGetValue(key, out var g))
                vGuides[key] = (g.x, Math.Min(g.y0, y0), Math.Max(g.y1, y1));
            else
                vGuides[key] = (x, y0, y1);
        }

        void AddH(double y, double xA0, double xA1, double xB0, double xB1)
        {
            var x0 = Math.Min(Math.Min(xA0, xA1), Math.Min(xB0, xB1)) - padMm;
            var x1 = Math.Max(Math.Max(xA0, xA1), Math.Max(xB0, xB1)) + padMm;
            var key = Bucket(y);
            if (hGuides.TryGetValue(key, out var g))
                hGuides[key] = (g.y, Math.Min(g.x0, x0), Math.Max(g.x1, x1));
            else
                hGuides[key] = (y, x0, x1);
        }

        foreach (var other in _project.Graph.Panels.Values)
        {
            if (ignoreIds.Contains(other.Id)) continue;
            var otherDef = _project.RequireDefinition(other.DefinitionId);
            var otherSize = GetLogicalSizeMm(otherDef, other.RotationDegrees);
            var ol = other.PositionXMm;
            var ot = other.PositionYMm;
            var oright = ol + otherSize.width;
            var ob = ot + otherSize.height;
            var ocx = (ol + oright) * 0.5;
            var ocy = (ot + ob) * 0.5;

            // Vertical guides (shared X)
            if (Math.Abs(ml - ol) <= tolMm) AddV(ml, mt, mb, ot, ob, extendBeyond: false);
            if (Math.Abs(mr - oright) <= tolMm) AddV(mr, mt, mb, ot, ob, extendBeyond: false);
            if (Math.Abs(ml - oright) <= tolMm) AddV(ml, mt, mb, ot, ob, extendBeyond: false);
            if (Math.Abs(mr - ol) <= tolMm) AddV(mr, mt, mb, ot, ob, extendBeyond: false);
            if (Math.Abs(mcx - ocx) <= tolMm) AddV(mcx, mt, mb, ot, ob, extendBeyond: true);

            // Horizontal guides (shared Y) — top / center / bottom like Canva
            if (Math.Abs(mt - ot) <= tolMm) AddH(mt, ml, mr, ol, oright);
            if (Math.Abs(mb - ob) <= tolMm) AddH(mb, ml, mr, ol, oright);
            if (Math.Abs(mt - ob) <= tolMm) AddH(mt, ml, mr, ol, oright);
            if (Math.Abs(mb - ot) <= tolMm) AddH(mb, ml, mr, ol, oright);
            if (Math.Abs(mcy - ocy) <= tolMm) AddH(mcy, ml, mr, ol, oright);
        }

        // Soft magenta — visible but not shouty (Canva-like).
        var stroke = new SolidColorBrush(Color.FromArgb(0xB8, 0xD9, 0x46, 0xEF));
        stroke.Freeze();
        var dash = new DoubleCollection { 1.5, 2.75 };

        foreach (var (_, g) in vGuides)
        {
            var (x1, y1) = WorldToCanvas(g.x, g.y0);
            var (x2, y2) = WorldToCanvas(g.x, g.y1);
            AddAlignmentGuideLine(x1, y1, x2, y2, stroke, dash);
        }

        foreach (var (_, g) in hGuides)
        {
            var (x1, y1) = WorldToCanvas(g.x0, g.y);
            var (x2, y2) = WorldToCanvas(g.x1, g.y);
            AddAlignmentGuideLine(x1, y1, x2, y2, stroke, dash);
        }
    }

    private void AddAlignmentGuideLine(
        double x1, double y1, double x2, double y2, Brush stroke, DoubleCollection dash)
    {
        var line = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = 1.0,
            StrokeDashArray = dash,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        Panel.SetZIndex(line, 2500);
        DesignCanvas.Children.Add(line);
        _alignmentGuideVisuals.Add(line);
    }

    private void ClearAlignmentGuides()
    {
        foreach (var el in _alignmentGuideVisuals)
            DesignCanvas.Children.Remove(el);
        _alignmentGuideVisuals.Clear();
    }

    private static (double width, double height) GetLogicalSizeMm(SolarPanelDefinition def, int rotation)
    {
        var rot = ((rotation % 180) + 180) % 180;
        return rot == 90 ? (def.HeightMm, def.WidthMm) : (def.WidthMm, def.HeightMm);
    }

    private Guid? FindPanelAt(Point canvasPoint)
    {
        foreach (var visual in _panelVisuals.Values.Reverse())
        {
            var left = Canvas.GetLeft(visual.Root);
            var top = Canvas.GetTop(visual.Root);
            if (canvasPoint.X >= left && canvasPoint.X <= left + visual.Body.Width &&
                canvasPoint.Y >= top && canvasPoint.Y <= top + visual.Body.Height)
            {
                return visual.InstanceId;
            }
        }
        return null;
    }

    private void SelectConnection(Guid id)
    {
        var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                       || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (additive)
        {
            if (_selectedConnectionIds.Contains(id))
                _selectedConnectionIds.Remove(id);
            else
                _selectedConnectionIds.Add(id);
            RefreshAll();
            return;
        }

        SetSelection(connections: new[] { id });
    }

    private void SetSelection(IEnumerable<Guid>? panels = null, IEnumerable<Guid>? connections = null)
    {
        _selectedPanelIds.Clear();
        _selectedConnectionIds.Clear();
        _selectedEquipmentIds.Clear();
        if (panels is not null)
        {
            foreach (var id in panels)
                _selectedPanelIds.Add(id);
        }
        if (connections is not null)
        {
            foreach (var id in connections)
                _selectedConnectionIds.Add(id);
        }
        RefreshAll();
    }

    private void ClearTransientSelection()
    {
        _selectedPanelIds.Clear();
        _selectedConnectionIds.Clear();
        _selectedEquipmentIds.Clear();
        _selectedWaypointConnectionId = null;
        _selectedWaypointIndex = null;
        _draggingPanelId = null;
        _rotatingEquipmentId = null;
        _dragOrigins.Clear();
        ClearAlignmentGuides();
        ClearRotateDegreeLabel();
    }

    private bool DeleteSelection()
    {
        var deleted = false;

        // Prefer deleting a selected waypoint over the whole wire.
        if (_selectedWaypointConnectionId is Guid wpConn
            && _selectedWaypointIndex is int wpIndex
            && _project.Graph.Connections.TryGetValue(wpConn, out var wpWire)
            && wpIndex >= 0
            && wpIndex < wpWire.Wire.Waypoints.Count)
        {
            wpWire.Wire.Waypoints.RemoveAt(wpIndex);
            _selectedWaypointConnectionId = null;
            _selectedWaypointIndex = null;
            _project.NotifyChanged("Delete wire waypoint");
            RefreshAll();
            return true;
        }

        foreach (var connId in _selectedConnectionIds.ToList())
        {
            if (_project.Graph.Connections.ContainsKey(connId))
            {
                _project.History.Execute(new DisconnectCommand(_project, connId));
                deleted = true;
            }
        }

        foreach (var panelId in _selectedPanelIds.ToList())
        {
            if (_project.Graph.Panels.ContainsKey(panelId))
            {
                _project.History.Execute(new DeletePanelCommand(_project, panelId));
                deleted = true;
            }
        }

        if (_selectedObstacleId is Guid obstacleId && GetActiveRoofSurface() is { } obstacleRoof)
        {
            obstacleRoof.RemoveObstacle(obstacleId);
            _selectedObstacleId = null;
            _project.NotifyChanged("Delete obstacle");
            deleted = true;
        }

        foreach (var equipmentId in _selectedEquipmentIds.ToList())
        {
            if (_project.Graph.RemoveEquipment(equipmentId))
            {
                deleted = true;
                _project.NotifyChanged("Delete equipment");
            }
        }

        if (!deleted) return false;

        ClearTransientSelection();
        RefreshAll();
        return true;
    }

    private void BeginMarquee(Point start)
    {
        _isMarqueeSelecting = true;
        _marqueeStart = start;
        _marqueeRect = new Rectangle
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 0xF5, 0x9E, 0x0B)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_marqueeRect, start.X);
        Canvas.SetTop(_marqueeRect, start.Y);
        _marqueeRect.Width = 0;
        _marqueeRect.Height = 0;
        DesignCanvas.Children.Add(_marqueeRect);
    }

    private void UpdateMarquee(Point current)
    {
        if (_marqueeRect is null) return;

        var x = Math.Min(_marqueeStart.X, current.X);
        var y = Math.Min(_marqueeStart.Y, current.Y);
        var w = Math.Abs(current.X - _marqueeStart.X);
        var h = Math.Abs(current.Y - _marqueeStart.Y);
        Canvas.SetLeft(_marqueeRect, x);
        Canvas.SetTop(_marqueeRect, y);
        _marqueeRect.Width = w;
        _marqueeRect.Height = h;

        // Live preview of what would be selected
        var rect = new Rect(x, y, w, h);
        var panels = FindPanelsInRect(rect);
        var wires = FindWiresInRect(rect);
        var equipment = FindEquipmentInRect(rect);

        // Temporary highlight without committing until mouse up
        _selectedPanelIds.Clear();
        _selectedConnectionIds.Clear();
        _selectedEquipmentIds.Clear();
        foreach (var id in panels) _selectedPanelIds.Add(id);
        foreach (var id in wires) _selectedConnectionIds.Add(id);
        foreach (var id in equipment) _selectedEquipmentIds.Add(id);

        // Update visuals without rebuilding everything heavily
        foreach (var visual in _panelVisuals.Values)
            UpdatePanelVisual(visual, _project.Graph.GetPanel(visual.InstanceId));
        foreach (var visual in _equipmentVisuals.Values)
        {
            if (_project.Graph.TryGetEquipment(visual.InstanceId, out var eq))
                UpdateEquipmentVisual(visual, eq);
        }
        RebuildWireVisuals();
        if (_marqueeRect is not null && !DesignCanvas.Children.Contains(_marqueeRect))
            DesignCanvas.Children.Add(_marqueeRect);
        else if (_marqueeRect is not null)
            Panel.SetZIndex(_marqueeRect, 1000);

        RefreshStatusAndInspector();
    }

    private void EndMarquee(bool commit)
    {
        _isMarqueeSelecting = false;
        if (_marqueeRect is not null)
        {
            DesignCanvas.Children.Remove(_marqueeRect);
            _marqueeRect = null;
        }

        if (!commit)
        {
            _selectedPanelIds.Clear();
            _selectedConnectionIds.Clear();
            _selectedEquipmentIds.Clear();
        }
        // On commit, selection sets already filled by UpdateMarquee
    }

    private List<Guid> FindPanelsInRect(Rect rect)
    {
        var result = new List<Guid>();
        foreach (var visual in _panelVisuals.Values)
        {
            var left = Canvas.GetLeft(visual.Root);
            var top = Canvas.GetTop(visual.Root);
            var panelRect = new Rect(left, top, visual.Body.Width, visual.Body.Height);
            if (rect.IntersectsWith(panelRect))
                result.Add(visual.InstanceId);
        }
        return result;
    }

    private List<Guid> FindWiresInRect(Rect rect)
    {
        var result = new List<Guid>();
        foreach (var (id, visual) in _wireVisuals)
        {
            if (visual.HitPoints.Count == 0) continue;
            double minX = visual.HitPoints[0].X, maxX = minX;
            double minY = visual.HitPoints[0].Y, maxY = minY;
            foreach (var p in visual.HitPoints)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }
            var wireBounds = new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            if (wireBounds.Width < 8) wireBounds.Inflate(4, 0);
            if (wireBounds.Height < 8) wireBounds.Inflate(0, 4);
            if (rect.IntersectsWith(wireBounds))
                result.Add(id);
        }
        return result;
    }

    private List<Guid> FindEquipmentInRect(Rect rect)
    {
        var result = new List<Guid>();
        foreach (var visual in _equipmentVisuals.Values)
        {
            var left = Canvas.GetLeft(visual.Root);
            var top = Canvas.GetTop(visual.Root);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;
            var w = visual.Body.Width;
            var h = visual.Body.Height;
            if (w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h)) continue;
            if (rect.IntersectsWith(new Rect(left, top, w, h)))
                result.Add(visual.InstanceId);
        }
        return result;
    }

    private static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

    private void BeginEquipmentInteraction(Guid equipmentId, Point canvasPos, bool additive)
    {
        if (!additive)
        {
            _selectedPanelIds.Clear();
            _selectedConnectionIds.Clear();
            _selectedEquipmentIds.Clear();
        }

        _selectedEquipmentIds.Add(equipmentId);
        _selectedObstacleId = null;

        _draggingPanelId = equipmentId;
        _dragStartMouse = canvasPos;
        _dragOrigins.Clear();
        if (_project.Graph.TryGetEquipment(equipmentId, out var eq))
            _dragOrigins[equipmentId] = (eq.PositionXMm, eq.PositionYMm);
        _dragMoved = false;
        DesignCanvas.CaptureMouse();
        RefreshAll();
    }

    private void AddCombiner_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm();
        PlaceAndSelectEquipment(_project.AddCombiner(x, y));
    }

    private void AddDisconnect_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "PV array DC isolators are not one-size-fits-all.\n\n" +
            "Common ratings are around 1000 V DC with current options such as " +
            "10 A, 16 A, 20 A, 25 A, 30 A, 32 A, 40 A, 50 A, or 60 A.\n\n" +
            "Match the isolator to your string Voc (cold) and Isc — and to the " +
            "inverter/combiner path. This app is a design aid only, not code approval.",
            "Check your DC isolator rating",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        var (x, y) = NextEquipmentPlaceMm(700);
        PlaceAndSelectEquipment(_project.AddPvDisconnect(x, y));
    }

    private void AddBranchYPos_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(400);
        PlaceAndSelectEquipment(_project.AddBranchY(x, y, Polarity.Positive));
    }

    private void AddBranchYNeg_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(400);
        PlaceAndSelectEquipment(_project.AddBranchY(x, y, Polarity.Negative));
    }

    private void AddInverter5k_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(1200);
        PlaceAndSelectEquipment(_project.AddStringInverter(x, y, InverterDefinition.CreateGeneric5kW2Mppt()));
    }

    private void AddInverter76k_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(1200);
        PlaceAndSelectEquipment(_project.AddStringInverter(x, y, InverterDefinition.CreateGeneric7_6kW3Mppt()));
    }

    private void AddInverterAnenji12k_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(900);
        PlaceAndSelectEquipment(_project.AddStringInverter(x, y, InverterDefinition.CreateAnenji12kW2Mppt()));
    }

    private void AddInverterAnenji4_2k_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(900);
        PlaceAndSelectEquipment(_project.AddStringInverter(x, y, InverterDefinition.CreateAnenji4_2kW1Mppt()));
    }

    private void AddInverterAnenji6_5k_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(900);
        PlaceAndSelectEquipment(_project.AddStringInverter(x, y, InverterDefinition.CreateAnenji6_5kW2Mppt()));
    }

    private void RebuildEquipmentVisuals()
    {
        if (!ShowsEquipment)
        {
            foreach (var visual in _equipmentVisuals.Values.ToList())
                DesignCanvas.Children.Remove(visual.Root);
            _equipmentVisuals.Clear();
            return;
        }

        foreach (var id in _equipmentVisuals.Keys.ToList())
        {
            if (!_project.Graph.Equipment.ContainsKey(id))
            {
                DesignCanvas.Children.Remove(_equipmentVisuals[id].Root);
                _equipmentVisuals.Remove(id);
            }
        }

        foreach (var equipment in _project.Graph.Equipment.Values)
        {
            if (!_equipmentVisuals.TryGetValue(equipment.Id, out var visual))
            {
                visual = CreateEquipmentVisual(equipment);
                _equipmentVisuals[equipment.Id] = visual;
                DesignCanvas.Children.Add(visual.Root);
            }

            UpdateEquipmentVisual(visual, equipment);
        }
    }

    private EquipmentVisual CreateEquipmentVisual(ElectricalEquipmentInstance equipment)
    {
        var root = new Canvas
        {
            Cursor = Cursors.SizeAll,
            Background = Brushes.Transparent, // required so empty canvas chrome still hit-tests
        };
        var isInverter = equipment.Kind == EquipmentKind.StringInverter;
        var isStorage = equipment.Kind is EquipmentKind.Battery or EquipmentKind.BatteryDisconnect;
        var isCombiner = equipment.Kind == EquipmentKind.CombinerBox;
        var isAnenji = IsAnenjiHybridFace(equipment);
        var isBatteryFace = equipment.Kind == EquipmentKind.Battery;
        var isPvIsolatorFace = equipment.Kind == EquipmentKind.PvDisconnect;
        var isBattDiscFace = equipment.Kind == EquipmentKind.BatteryDisconnect;
        var photoFace = isCombiner || isAnenji || isBatteryFace || isPvIsolatorFace || isBattDiscFace;
        ImageBrush? faceBrush = null;
        if (isCombiner) faceBrush = CreateCombinerFaceBrush();
        else if (isAnenji) faceBrush = CreateAnenjiFaceBrush(equipment);
        else if (isBatteryFace) faceBrush = CreateBatteryFaceBrush(equipment);
        else if (isPvIsolatorFace) faceBrush = CreateDisconnectFaceBrush();
        else if (isBattDiscFace) faceBrush = CreateBatteryDisconnectFaceBrush();

        var body = new Border
        {
            Background = faceBrush is not null
                ? Brushes.Transparent
                : isInverter
                    ? new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF5))
                    : isStorage && !isBattDiscFace
                        ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xEE))
                        : new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1)),
            BorderBrush = photoFace
                ? Brushes.Transparent
                : isInverter
                    ? new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F))
                    : isStorage
                        ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x4F))
                        : new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A)),
            BorderThickness = new Thickness(photoFace ? 0 : 2),
            CornerRadius = new CornerRadius(photoFace ? 0 : 4),
            Cursor = Cursors.SizeAll,
            ClipToBounds = true,
        };
        var title = new TextBlock
        {
            Text = equipment.Name,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = photoFace
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)),
            Background = photoFace
                ? new SolidColorBrush(Color.FromArgb(160, 20, 20, 24))
                : Brushes.Transparent,
            Padding = photoFace ? new Thickness(6, 3, 6, 3) : new Thickness(0),
            Margin = new Thickness(8, 6, 8, 0),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        if (faceBrush is not null)
        {
            var face = new Image
            {
                Source = faceBrush.ImageSource,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.HighQuality);
            var layer = new Grid();
            layer.Children.Add(face);
            layer.Children.Add(title);
            body.Child = layer;
        }
        else
        {
            body.Child = title;
        }
        root.Children.Add(body);

        var rotateStem = new Line
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        var rotateHandle = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = Brushes.White,
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 2,
            Cursor = Cursors.Arrow,
            Visibility = Visibility.Collapsed,
            ToolTip = "Drag to rotate · snaps to 90° · Shift=15° · Alt=free · R / Shift+R nudge",
        };
        root.Children.Add(rotateStem);
        root.Children.Add(rotateHandle);

        var portEllipses = new Dictionary<Guid, Ellipse>();
        foreach (var port in equipment.Ports)
        {
            var ellipse = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = port.Polarity == Polarity.Positive
                    ? (Brush)FindResource("PositiveBrush")
                    : (Brush)FindResource("NegativeBrush"),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.Cross,
                Tag = port.Id,
            };
            ellipse.MouseLeftButtonDown += Port_MouseLeftButtonDown;
            root.Children.Add(ellipse);
            portEllipses[port.Id] = ellipse;
        }

        var rotateTransform = new RotateTransform(0);
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = rotateTransform;

        var visual = new EquipmentVisual(
            equipment.Id, root, body, title, portEllipses, rotateHandle, rotateStem, rotateTransform);

        rotateHandle.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            BeginEquipmentRotation(equipment.Id, e.GetPosition(DesignCanvas));
            e.Handled = true;
        };

        void OnEquipmentMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var pos = e.GetPosition(DesignCanvas);
            BeginEquipmentInteraction(
                equipment.Id,
                pos,
                additive: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
        }

        root.MouseLeftButtonDown += OnEquipmentMouseDown;
        body.MouseLeftButtonDown += OnEquipmentMouseDown;
        root.MouseEnter += (_, _) => SetEquipmentPortsVisible(visual, true);
        root.MouseLeave += (_, _) =>
        {
            if (!_selectedEquipmentIds.Contains(visual.InstanceId) && _wireFromPortId is null)
                SetEquipmentPortsVisible(visual, false);
        };
        return visual;
    }

    private void UpdateEquipmentVisual(EquipmentVisual visual, ElectricalEquipmentInstance equipment)
    {
        var w = Math.Max(equipment.WidthMm * MmToPx * _zoom, 24);
        var h = Math.Max(equipment.HeightMm * MmToPx * _zoom, 24);
        visual.Body.Width = w;
        visual.Body.Height = h;
        visual.Title.Text = equipment.Name;
        visual.Title.FontSize = 11;
        visual.Title.Margin = new Thickness(6, 4, 4, 0);

        var (x, y) = WorldToCanvas(equipment.PositionXMm, equipment.PositionYMm);
        Canvas.SetLeft(visual.Root, x);
        Canvas.SetTop(visual.Root, y);
        visual.Root.Width = w;
        visual.Root.Height = h;
        Panel.SetZIndex(visual.Root, 20);

        var selected = _selectedEquipmentIds.Contains(equipment.Id);
        var isCombiner = equipment.Kind == EquipmentKind.CombinerBox;
        var isAnenji = IsAnenjiHybridFace(equipment);
        var isBatteryFace = equipment.Kind == EquipmentKind.Battery;
        var isPvIsolatorFace = equipment.Kind == EquipmentKind.PvDisconnect;
        var isBattDiscFace = equipment.Kind == EquipmentKind.BatteryDisconnect;
        var photoFace = isCombiner || isAnenji || isBatteryFace || isPvIsolatorFace || isBattDiscFace;
        visual.Body.BorderBrush = selected
            ? (Brush)FindResource("AccentBrush")
            : photoFace
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A));
        visual.Body.BorderThickness = new Thickness(selected ? 3 : photoFace ? 0 : 2);
        visual.Body.CornerRadius = new CornerRadius(photoFace ? 0 : 4);

        var portSize = photoFace
            ? Math.Clamp(8 * _zoom, 4, 9)
            : Math.Clamp(10 * _zoom, 5, 12);
        var half = portSize / 2.0;

        static bool IsOutputPort(ElectricalPort port) =>
            port.PortType is PortType.OutputPositive
                or PortType.OutputNegative
                or PortType.DisconnectOutPositive
                or PortType.DisconnectOutNegative
                or PortType.BranchOut
            || port.Label.StartsWith("OUT", StringComparison.OrdinalIgnoreCase);

        if (isCombiner)
        {
            LayoutCombinerBottomPorts(visual, equipment, w, h, portSize, half);
        }
        else if (equipment.Kind == EquipmentKind.StringInverter)
        {
            // All inverter terminals (MPPT / AC / BAT) sit on the bottom edge — never side columns.
            LayoutInverterBottomPorts(visual, equipment, w, h, portSize, half);
        }
        else if (isBatteryFace)
        {
            if (ElectricalEquipmentInstance.IsLandscapePrismaticBattery(equipment))
                LayoutBatteryTopLeftRightPorts(visual, equipment, w, h, portSize, half);
            else if (ElectricalEquipmentInstance.IsRackBattery(equipment))
                LayoutBatteryRackTopDualPorts(visual, equipment, w, h, portSize, half);
            else if (ElectricalEquipmentInstance.IsWall10kWBattery(equipment))
                LayoutBatteryTopDualPorts(visual, equipment, w, h, portSize, half);
            else
                LayoutBatteryBottomDualPorts(visual, equipment, w, h, portSize, half);
        }
        else if (isPvIsolatorFace)
        {
            LayoutDisconnectPorts(visual, equipment, w, h, portSize, half);
        }
        else if (isBattDiscFace)
        {
            LayoutBatteryDisconnectPorts(visual, equipment, w, h, portSize, half);
        }
        else
        {
            var inputs = equipment.Ports.Where(p => !IsOutputPort(p)).ToList();
            var outputs = equipment.Ports.Where(IsOutputPort).ToList();
            LayoutEquipmentPortColumn(visual, inputs, leftSide: true, w, h, portSize, half);
            LayoutEquipmentPortColumn(visual, outputs, leftSide: false, w, h, portSize, half);
        }

        visual.RotateTransform.Angle = equipment.RotationDegrees;

        var handleSize = Math.Clamp(11 * _zoom, 8, 14);
        visual.RotateHandle.Width = handleSize;
        visual.RotateHandle.Height = handleSize;
        var handleOffset = Math.Max(18, 22 * _zoom);
        Canvas.SetLeft(visual.RotateHandle, w / 2 - handleSize / 2);
        Canvas.SetTop(visual.RotateHandle, -handleOffset - handleSize / 2);
        visual.RotateStem.X1 = w / 2;
        visual.RotateStem.Y1 = 0;
        visual.RotateStem.X2 = w / 2;
        visual.RotateStem.Y2 = -handleOffset;

        var showRotate = selected;
        visual.RotateHandle.Visibility = showRotate ? Visibility.Visible : Visibility.Collapsed;
        visual.RotateStem.Visibility = showRotate ? Visibility.Visible : Visibility.Collapsed;

        if (selected || _wireFromPortId is not null)
            SetEquipmentPortsVisible(visual, true);
    }

    private void BeginEquipmentRotation(Guid equipmentId, Point canvasPos)
    {
        if (!_project.Graph.TryGetEquipment(equipmentId, out var eq)) return;
        if (!_equipmentVisuals.TryGetValue(equipmentId, out var visual)) return;

        _selectedPanelIds.Clear();
        _selectedConnectionIds.Clear();
        _selectedEquipmentIds.Clear();
        _selectedEquipmentIds.Add(equipmentId);
        _selectedObstacleId = null;
        _draggingPanelId = null;
        _dragOrigins.Clear();

        var center = GetEquipmentCanvasCenter(visual);
        _rotatingEquipmentId = equipmentId;
        _rotateStartMouseAngleDeg = Math.Atan2(canvasPos.Y - center.Y, canvasPos.X - center.X) * (180.0 / Math.PI);
        _rotateStartEquipmentDeg = eq.RotationDegrees;
        _rotateMoved = false;
        DesignCanvas.CaptureMouse();
        UpdateRotateDegreeLabel(GetEquipmentCanvasCenter(visual), eq.RotationDegrees);
        RefreshAll();
    }

    private void UpdateRotateDegreeLabel(Point center, double degrees)
    {
        if (_rotateDegreeLabel is null)
        {
            _rotateDegreeLabel = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                Padding = new Thickness(6, 3, 6, 3),
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_rotateDegreeLabel);
            Panel.SetZIndex(_rotateDegreeLabel, 2000);
        }

        _rotateDegreeLabel.Text = $"{degrees:0}°";
        Canvas.SetLeft(_rotateDegreeLabel, center.X + 16);
        Canvas.SetTop(_rotateDegreeLabel, center.Y - 28);
    }

    private void ClearRotateDegreeLabel()
    {
        if (_rotateDegreeLabel is null) return;
        DesignCanvas.Children.Remove(_rotateDegreeLabel);
        _rotateDegreeLabel = null;
    }

    private static Point GetEquipmentCanvasCenter(EquipmentVisual visual)
    {
        var left = Canvas.GetLeft(visual.Root);
        var top = Canvas.GetTop(visual.Root);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        return new Point(left + visual.Body.Width / 2, top + visual.Body.Height / 2);
    }

    /// <summary>
    /// Measured X centers (fraction of image width) for the 6 MC4 string glands
    /// and 2 main DC glands on Assets/combiner-6string.png.
    /// </summary>
    private static readonly double[] Combiner6StringX = [0.128, 0.226, 0.326, 0.420, 0.521, 0.618];
    private static readonly double[] Combiner6OutX = [0.733, 0.823];

    private static ImageBrush CreateCombinerFaceBrush()
    {
        var bmp = LoadEquipmentFaceBitmap("combiner-6string.png");
        return new ImageBrush(bmp)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
    }

    private static ImageBrush CreateAnenjiFaceBrush(ElectricalEquipmentInstance equipment)
    {
        var id = equipment.InverterSpecs?.DefinitionId;
        var asset = id == InverterDefinition.Anenji4_2kWDefinitionId
            ? "inverter-anenji-4_2kw.png"
            : id == InverterDefinition.Anenji6_5kWDefinitionId
                ? "inverter-anenji-6_5kw.png"
                : "inverter-anenji-12kw.png";
        var bmp = LoadEquipmentFaceBitmap(asset);
        return new ImageBrush(bmp)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
    }

    private static ImageBrush CreateBatteryFaceBrush(ElectricalEquipmentInstance? equipment = null)
    {
        var asset = "battery-anenji-16kwh.png";
        if (equipment is not null)
        {
            if (ElectricalEquipmentInstance.IsLandscapePrismaticBattery(equipment))
                asset = "battery-anenji-12_8v-300ah.png";
            else if (ElectricalEquipmentInstance.IsRackBattery(equipment))
                asset = "battery-anenji-5_1kwh-rack.png";
            else if (ElectricalEquipmentInstance.IsWall10kWBattery(equipment))
                asset = "battery-anenji-10kw.png";
        }

        var bmp = LoadEquipmentFaceBitmap(asset);
        return new ImageBrush(bmp)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
    }

    private static ImageBrush CreateDisconnectFaceBrush()
    {
        var bmp = LoadEquipmentFaceBitmap("disconnect-pv-isolator.png");
        return new ImageBrush(bmp)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
    }

    private static ImageBrush CreateBatteryDisconnectFaceBrush()
    {
        var bmp = LoadEquipmentFaceBitmap("battery-disconnect-dhm1b.png");
        return new ImageBrush(bmp)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
    }

    private static System.Windows.Media.Imaging.BitmapSource LoadEquipmentFaceBitmap(string assetFile)
    {
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri($"pack://application:,,,/Assets/{assetFile}", UriKind.Absolute);
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat;
        bmp.EndInit();
        // Pbgra32 so transparent cutouts composite cleanly (no black matte in WPF).
        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            bmp,
            PixelFormats.Pbgra32,
            null,
            0);
        converted.Freeze();
        return converted;
    }

    private static bool IsAnenjiHybridFace(ElectricalEquipmentInstance equipment)
    {
        if (equipment.Kind != EquipmentKind.StringInverter) return false;
        var id = equipment.InverterSpecs?.DefinitionId;
        if (id == InverterDefinition.Anenji12kWDefinitionId
            || id == InverterDefinition.Anenji4_2kWDefinitionId
            || id == InverterDefinition.Anenji6_5kWDefinitionId)
            return true;
        return equipment.Ports.Any(p => p.Label.Equals("BAT+", StringComparison.OrdinalIgnoreCase))
            && equipment.Ports.Any(p => p.Label.Equals("AC IN L", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every string-inverter terminal along the bottom edge (left → right).
    /// Hybrids: AC IN · AC OUT · (PV) · BAT · (extra PV). Generics: MPPT rows then BAT.
    /// </summary>
    private static void LayoutInverterBottomPorts(
        EquipmentVisual visual,
        ElectricalEquipmentInstance equipment,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        // Sit on the bottom seam (slightly overlapping the face edge).
        var y = bodyH - half;
        var pairGap = Math.Min(portSize * 0.9, bodyW * 0.025);
        var placed = new HashSet<Guid>();
        var hybrid = equipment.Ports.Any(p =>
            p.Label.Equals("AC IN L", StringComparison.OrdinalIgnoreCase));
        var mpptCount = equipment.InverterSpecs?.MpptCount
            ?? Math.Max(1, equipment.Ports.Count(p =>
                p.Label.StartsWith("MPPT", StringComparison.OrdinalIgnoreCase)
                && p.Label.EndsWith("+", StringComparison.Ordinal)));
        var singlePv = mpptCount <= 1
            || equipment.InverterSpecs?.DefinitionId == InverterDefinition.Anenji4_2kWDefinitionId;

        void Place(string label, double centerX)
        {
            var port = equipment.Ports.FirstOrDefault(p =>
                p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (port is null || !visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) return;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            ellipse.ToolTip = label.StartsWith("MPPT1", StringComparison.OrdinalIgnoreCase) ? $"PV1 ({label})"
                : label.StartsWith("MPPT2", StringComparison.OrdinalIgnoreCase) ? $"PV2 ({label})"
                : label;
            Canvas.SetLeft(ellipse, centerX - half);
            Canvas.SetTop(ellipse, y);
            placed.Add(port.Id);
        }

        void PlacePair(string plusLabel, string minusLabel, double centerX)
        {
            Place(plusLabel, centerX - pairGap);
            Place(minusLabel, centerX + pairGap);
        }

        if (hybrid && singlePv)
        {
            // 4.2 kW: AC IN / AC OUT / single PV on the left · BAT in the middle.
            PlacePair("AC IN L", "AC IN N", 0.10 * bodyW);
            PlacePair("AC OUT L", "AC OUT N", 0.22 * bodyW);
            PlacePair("MPPT1+", "MPPT1-", 0.34 * bodyW);
            PlacePair("BAT+", "BAT-", 0.55 * bodyW);
        }
        else if (hybrid)
        {
            // 6.5 / 12 kW (+): AC left · BAT middle · PV1 / PV2 right.
            PlacePair("AC IN L", "AC IN N", 0.12 * bodyW);
            PlacePair("AC OUT L", "AC OUT N", 0.28 * bodyW);
            PlacePair("BAT+", "BAT-", 0.50 * bodyW);
            PlacePair("MPPT1+", "MPPT1-", 0.72 * bodyW);
            PlacePair("MPPT2+", "MPPT2-", 0.88 * bodyW);
            for (var i = 3; i <= mpptCount; i++)
            {
                var t = 0.88 + (i - 2) * 0.06;
                if (t > 0.96) t = 0.96;
                PlacePair($"MPPT{i}+", $"MPPT{i}-", t * bodyW);
            }
        }
        else
        {
            // Generic string inverter: MPPT pairs across the bottom, BAT at the right.
            var slots = mpptCount + 1; // + BAT
            for (var i = 1; i <= mpptCount; i++)
            {
                var t = (i - 0.5) / slots;
                PlacePair($"MPPT{i}+", $"MPPT{i}-", t * bodyW);
            }

            PlacePair("BAT+", "BAT-", ((slots - 0.5) / slots) * bodyW);
        }

        // Safety: anything not placed yet still goes on the bottom (never left stranded at 0,0).
        var leftovers = equipment.Ports.Where(p => !placed.Contains(p.Id)).ToList();
        if (leftovers.Count == 0) return;
        for (var i = 0; i < leftovers.Count; i++)
        {
            var port = leftovers[i];
            if (!visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) continue;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            ellipse.ToolTip = port.Label;
            var t = leftovers.Count == 1 ? 0.5 : (i + 0.5) / leftovers.Count;
            Canvas.SetLeft(ellipse, t * bodyW - half);
            Canvas.SetTop(ellipse, y);
        }
    }

    private static void LayoutBatteryBottomDualPorts(
        EquipmentVisual visual,
        ElectricalEquipmentInstance equipment,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        var y = bodyH - half * 0.35;
        var pairGap = Math.Min(portSize * 0.95, bodyW * 0.03);

        void Place(string label, double centerX)
        {
            var port = equipment.Ports.FirstOrDefault(p =>
                p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (port is null || !visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) return;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            ellipse.ToolTip = label;
            Canvas.SetLeft(ellipse, centerX - half);
            Canvas.SetTop(ellipse, y);
        }

        // 16 kWh: two −/+ pairs along the bottom (parallel lugs).
        Place("BAT1-", 0.28 * bodyW - pairGap);
        Place("BAT1+", 0.28 * bodyW + pairGap);
        Place("BAT2-", 0.72 * bodyW - pairGap);
        Place("BAT2+", 0.72 * bodyW + pairGap);
    }

    /// <summary>
    /// 12.8V 300Ah prismatic: BAT− far left, BAT+ far right on the top edge.
    /// </summary>
    private static void LayoutBatteryTopLeftRightPorts(
        EquipmentVisual visual,
        ElectricalEquipmentInstance equipment,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        var y = half * 0.35;

        void Place(string label, double centerX)
        {
            var port = equipment.Ports.FirstOrDefault(p =>
                p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (port is null || !visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) return;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            ellipse.ToolTip = label;
            Canvas.SetLeft(ellipse, centerX - half);
            Canvas.SetTop(ellipse, y);
        }

        Place("BAT-", 0.06 * bodyW);
        Place("BAT+", 0.94 * bodyW);
    }

    /// <summary>10 kW wall: dual − left / dual + right on the top edge.</summary>
    private static void LayoutBatteryTopDualPorts(
        EquipmentVisual visual,
        ElectricalEquipmentInstance equipment,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        var y = half * 0.35;
        var pairGap = Math.Min(portSize * 0.95, bodyW * 0.035);

        void Place(string label, double centerX)
        {
            var port = equipment.Ports.FirstOrDefault(p =>
                p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (port is null || !visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) return;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            ellipse.ToolTip = label;
            Canvas.SetLeft(ellipse, centerX - half);
            Canvas.SetTop(ellipse, y);
        }

        Place("BAT1-", 0.18 * bodyW - pairGap);
        Place("BAT2-", 0.18 * bodyW + pairGap);
        Place("BAT1+", 0.82 * bodyW - pairGap);
        Place("BAT2+", 0.82 * bodyW + pairGap);
    }

    /// <summary>Rack 5.1 kWh: two − on top-left block, two + on top-right block.</summary>
    private static void LayoutBatteryRackTopDualPorts(
        EquipmentVisual visual,
        ElectricalEquipmentInstance equipment,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        var y = half * 0.45;
        var pairGap = Math.Min(portSize * 1.05, bodyW * 0.028);

        void Place(string label, double centerX)
        {
            var port = equipment.Ports.FirstOrDefault(p =>
                p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (port is null || !visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) return;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            ellipse.ToolTip = label;
            Canvas.SetLeft(ellipse, centerX - half);
            Canvas.SetTop(ellipse, y);
        }

        Place("BAT1-", 0.12 * bodyW - pairGap);
        Place("BAT2-", 0.12 * bodyW + pairGap);
        Place("BAT1+", 0.88 * bodyW - pairGap);
        Place("BAT2+", 0.88 * bodyW + pairGap);
    }

    /// <summary>
    /// MC4 glands on Assets/disconnect-pv-isolator.png — top IN±, bottom OUT±.
    /// </summary>
    private static void LayoutDisconnectPorts(
        EquipmentVisual visual,
        ElectricalEquipmentInstance equipment,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        const double leftX = 0.277;
        const double rightX = 0.69;

        void Place(string label, double xFrac, double y)
        {
            var port = equipment.Ports.FirstOrDefault(p =>
                p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (port is null || !visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) return;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            ellipse.ToolTip = label;
            Canvas.SetLeft(ellipse, xFrac * bodyW - half);
            Canvas.SetTop(ellipse, y);
        }

        var yTop = -half * 0.15;
        var yBot = bodyH - half * 0.35;
        Place("IN+", leftX, yTop);
        Place("IN-", rightX, yTop);
        Place("OUT+", leftX, yBot);
        Place("OUT-", rightX, yBot);
    }

    /// <summary>
    /// Top/bottom lug pairs on Assets/battery-disconnect-dhm1b.png (− left, + right).
    /// </summary>
    private static void LayoutBatteryDisconnectPorts(
        EquipmentVisual visual,
        ElectricalEquipmentInstance equipment,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        const double leftX = 0.26;
        const double rightX = 0.74;

        void Place(string label, double xFrac, double y)
        {
            var port = equipment.Ports.FirstOrDefault(p =>
                p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (port is null || !visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) return;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            ellipse.ToolTip = label;
            Canvas.SetLeft(ellipse, xFrac * bodyW - half);
            Canvas.SetTop(ellipse, y);
        }

        var yTop = half * 0.15;
        var yBot = bodyH - half * 0.5;
        Place("IN-", leftX, yTop);
        Place("IN+", rightX, yTop);
        Place("OUT-", leftX, yBot);
        Place("OUT+", rightX, yBot);
    }

    private static void LayoutCombinerBottomPorts(
        EquipmentVisual visual,
        ElectricalEquipmentInstance equipment,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        var positives = equipment.Ports
            .Where(p => p.PortType == PortType.StringInputPositive)
            .OrderBy(p => p.Label, StringComparer.Ordinal)
            .ToList();
        var negatives = equipment.Ports
            .Where(p => p.PortType == PortType.StringInputNegative)
            .OrderBy(p => p.Label, StringComparer.Ordinal)
            .ToList();
        var outPos = equipment.Ports.FirstOrDefault(p =>
            p.PortType == PortType.OutputPositive || p.Label.Equals("OUT+", StringComparison.OrdinalIgnoreCase));
        var outNeg = equipment.Ports.FirstOrDefault(p =>
            p.PortType == PortType.OutputNegative || p.Label.Equals("OUT-", StringComparison.OrdinalIgnoreCase));

        var stringCount = positives.Count;
        var stringXs = stringCount == Combiner6StringX.Length
            ? Combiner6StringX
            : Enumerable.Range(0, stringCount)
                .Select(i => 0.12 + (stringCount == 1 ? 0 : i * (0.50 / (stringCount - 1))))
                .ToArray();
        var outXs = Combiner6OutX;

        // Sit on the bottom lip so ports land on the photo’s protruding glands.
        var y = bodyH - half * 0.35;
        var pairGap = Math.Min(portSize * 0.95, bodyW * 0.028);

        void Place(ElectricalPort? port, double centerX)
        {
            if (port is null || !visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) return;
            ellipse.Width = portSize;
            ellipse.Height = portSize;
            Canvas.SetLeft(ellipse, centerX - half);
            Canvas.SetTop(ellipse, y);
        }

        for (var i = 0; i < stringCount; i++)
        {
            var cx = stringXs[i] * bodyW;
            Place(i < positives.Count ? positives[i] : null, cx - pairGap);
            Place(i < negatives.Count ? negatives[i] : null, cx + pairGap);
        }

        Place(outPos, outXs[0] * bodyW);
        Place(outNeg, outXs[1] * bodyW);
    }

    private static void LayoutEquipmentPortColumn(
        EquipmentVisual visual,
        IReadOnlyList<ElectricalPort> columnPorts,
        bool leftSide,
        double bodyW,
        double bodyH,
        double portSize,
        double half)
    {
        var n = columnPorts.Count;
        if (n == 0) return;

        var margin = Math.Max(half, Math.Min(bodyH * 0.12, 14));
        var usable = Math.Max(bodyH - margin * 2 - portSize, 0);

        for (var i = 0; i < n; i++)
        {
            var port = columnPorts[i];
            if (!visual.PortEllipses.TryGetValue(port.Id, out var ellipse)) continue;

            ellipse.Width = portSize;
            ellipse.Height = portSize;

            var y = n == 1
                ? (bodyH - portSize) / 2.0
                : margin + i * (usable / (n - 1));
            y = Math.Clamp(y, 0, Math.Max(0, bodyH - portSize));

            Canvas.SetLeft(ellipse, leftSide ? -half : bodyW - half);
            Canvas.SetTop(ellipse, y);
        }
    }

    private static void SetEquipmentPortsVisible(EquipmentVisual visual, bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var ellipse in visual.PortEllipses.Values)
            ellipse.Visibility = v;
    }

    private Guid? FindEquipmentAt(Point canvasPoint)
    {
        foreach (var visual in _equipmentVisuals.Values.Reverse())
        {
            var left = Canvas.GetLeft(visual.Root);
            var top = Canvas.GetTop(visual.Root);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;
            var w = visual.Body.Width;
            var h = visual.Body.Height;
            if (w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h)) continue;

            // Transform canvas point into the equipment's local (unrotated) space.
            var centerX = left + w / 2;
            var centerY = top + h / 2;
            var angleRad = -visual.RotateTransform.Angle * Math.PI / 180.0;
            var dx = canvasPoint.X - centerX;
            var dy = canvasPoint.Y - centerY;
            var localX = dx * Math.Cos(angleRad) - dy * Math.Sin(angleRad) + w / 2;
            var localY = dx * Math.Sin(angleRad) + dy * Math.Cos(angleRad) + h / 2;

            if (localX >= 0 && localX <= w && localY >= 0 && localY <= h)
                return visual.InstanceId;
        }
        return null;
    }

    private void DrawRoof_Click(object sender, RoutedEventArgs e)
    {
        SetUiTool(UiTool.Roof);
        _tool = CanvasTool.DrawRoof;
        var active = _project.Roofs.EnsureActiveRoof();
        if (active.IsClosed)
            active.OpenForEdit();
        ClearRoofLiveMeasure();
        UpdateToolButtonStyles();
        StatusText.Text = "DRAW ROOF  |  Ortho + ALIGN  |  Ctrl+Z / Backspace = undo last segment  |  Esc pauses (keeps outline)  |  Near start = CLOSE";
    }

    private void StraightenRoof_Click(object sender, RoutedEventArgs e)
    {
        if (!_project.Roofs.Roofs.Any(r => r.HasRoof))
        {
            StatusText.Text = "ROOF  |  Import or close a roof first";
            return;
        }

        if (_project.Roofs.Roofs.Any(r => r.HasRoof && r.IsLocked))
        {
            StatusText.Text = "ROOF  |  Unlock roof first to straighten";
            return;
        }

        _project.History.Execute(new StraightenRoofEdgesCommand(_project));
        FrameRoofsInView();
        RefreshAll();
        StatusText.Text = "ROOF  |  Edges straightened (axis-aligned)";
    }

    private void RotateRoof15_Click(object sender, RoutedEventArgs e)
    {
        if (!_project.Roofs.Roofs.Any(r => r.HasRoof))
        {
            StatusText.Text = "ROOF  |  Import or close a roof first";
            return;
        }

        // Rotate is intentional (button) — allowed even while locked.
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -15.0 : 15.0;
        _project.History.Execute(new RotateRoofsCommand(_project, step));
        RefreshAll();
        StatusText.Text = $"ROOF  |  Rotated {step:0}°";
    }

    private void ToggleRoofLock_Click(object sender, RoutedEventArgs e)
    {
        var roofs = _project.Roofs.Roofs.Where(r => r.HasRoof).ToList();
        if (roofs.Count == 0)
        {
            StatusText.Text = "ROOF  |  Import or close a roof first";
            return;
        }

        // If any unlocked → lock all (safe for wiring). Else unlock all for edits.
        var lockAll = roofs.Any(r => !r.IsLocked);
        foreach (var roof in roofs)
            roof.IsLocked = lockAll;

        _project.NotifyChanged(lockAll ? "Lock roof" : "Unlock roof");
        RefreshAll();
        StatusText.Text = lockAll
            ? "ROOF  |  Locked — corners/move blocked · ↻ rotate still works"
            : "ROOF  |  Unlocked — corners, Alt+drag move, and straighten enabled";
    }

    private void UpdateLockRoofButton()
    {
        if (LockRoofButton is null) return;
        var roofs = _project.Roofs.Roofs.Where(r => r.HasRoof).ToList();
        if (roofs.Count == 0)
        {
            LockRoofButton.Content = "Lock roof";
            LockRoofButton.IsEnabled = false;
            return;
        }

        LockRoofButton.IsEnabled = true;
        var anyUnlocked = roofs.Any(r => !r.IsLocked);
        LockRoofButton.Content = anyUnlocked ? "Lock roof" : "Unlock roof";
    }

    /// <summary>
    /// Fit all closed roofs in the visible canvas (centered in front of the user).
    /// </summary>
    private void FrameRoofsInView()
    {
        var verts = _project.Roofs.Roofs
            .Where(r => r.HasRoof)
            .SelectMany(r => r.Vertices)
            .ToList();
        if (verts.Count == 0) return;

        var minX = verts.Min(v => v.X);
        var maxX = verts.Max(v => v.X);
        var minY = verts.Min(v => v.Y);
        var maxY = verts.Max(v => v.Y);
        var widthMm = Math.Max(maxX - minX, 500);
        var heightMm = Math.Max(maxY - minY, 500);

        var viewW = DesignCanvas.ActualWidth;
        var viewH = DesignCanvas.ActualHeight;
        if (viewW < 40 || viewH < 40 || double.IsNaN(viewW) || double.IsNaN(viewH))
        {
            viewW = Math.Max(ActualWidth - 360, 640);
            viewH = Math.Max(ActualHeight - 80, 480);
        }

        const double padPx = 72;
        var zoomX = (viewW - 2 * padPx) / (widthMm * MmToPx);
        var zoomY = (viewH - 2 * padPx) / (heightMm * MmToPx);
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.12, 2.8);

        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        _panOffset = new Point(
            viewW / 2 - cx * MmToPx * _zoom,
            viewH / 2 - cy * MmToPx * _zoom);

        if (ZoomLabel is not null)
            ZoomLabel.Text = $"{_zoom * 100:0}%";
    }

    private void NewRoofLayer_Click(object sender, RoutedEventArgs e)
    {
        _project.Roofs.AddRoof();
        _tool = CanvasTool.Select;
        ClearRoofLiveMeasure();
        _project.NotifyChanged("New roof layer");
        RefreshAll();
    }

    private void DemoLRoof_Click(object sender, RoutedEventArgs e)
    {
        _tool = CanvasTool.Select;
        _project.CreateDemoLShapedRoof();
        _panOffset = new Point(80, 80);
        _zoom = 0.55;
        ClearRoofLiveMeasure();
        RefreshAll();
    }

    private void DemoRoof_Click(object sender, RoutedEventArgs e)
    {
        _tool = CanvasTool.Select;
        _project.CreateDemoRectangularRoof();
        // Frame the roof
        _panOffset = new Point(80, 80);
        _zoom = 0.55;
        RefreshAll();
    }

    private void CloseRoof_Click(object sender, RoutedEventArgs e)
    {
        if (GetActiveRoofSurface() is not { } active || active.Vertices.Count < 3)
        {
            MessageBox.Show(this, "Need at least 3 vertices to close the roof.", "Roof",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _project.History.Execute(new CloseRoofCommand(_project, active.Id));
        if (active.IsClosed)
        {
            _tool = CanvasTool.Select;
            ClearRoofLiveMeasure();
            RefreshAll();
        }
    }

    private void ClearRoof_Click(object sender, RoutedEventArgs e)
    {
        if (_project.Roofs.Roofs.Count == 0) return;

        var result = MessageBox.Show(this,
            "Clear all roof layers? This cannot be undone with Esc.",
            "Clear roofs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _project.Roofs.Clear();
        _tool = CanvasTool.Select;
        _selectedObstacleId = null;
        ClearRoofLiveMeasure();
        _project.NotifyChanged("Clear roofs");
        RefreshAll();
    }

    private const string GoogleSolarSetupGuideUrl =
        "https://developers.google.com/maps/documentation/solar/get-api-key";
    private const string GoogleSolarApiEnableUrl =
        "https://console.cloud.google.com/apis/library/solar.googleapis.com";
    private const string GoogleGeocodingApiEnableUrl =
        "https://console.cloud.google.com/apis/library/geocoding-backend.googleapis.com";
    private const string GoogleMapsJsApiEnableUrl =
        "https://console.cloud.google.com/apis/library/maps-backend.googleapis.com";
    private const string GoogleCredentialsUrl =
        "https://console.cloud.google.com/apis/credentials";

    private void SetGoogleApiKey_Click(object sender, RoutedEventArgs e) => ShowGoogleApiKeyDialog();

    private void ShowGoogleApiKeyDialog()
    {
        var current = GoogleSolarApiKeyStore.TryResolve() ?? "";
        var masked = current.Length <= 8 ? current : current[..4] + "…" + current[^4..];

        var dialog = new Window
        {
            Title = "Google API key",
            Width = 520,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("BgBrush"),
            FontFamily = (FontFamily)FindResource("UiFont"),
        };

        var root = new DockPanel { Margin = new Thickness(18) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var save = new Button
        {
            Content = "Save key",
            Width = 100,
            Height = 32,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("PrimaryButton"),
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 88,
            Height = 32,
            IsCancel = true,
            Style = (Style)FindResource("GhostButton"),
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Get a Google Cloud API key (Solar API enabled), then paste it below.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 13.5,
            Margin = new Thickness(0, 0, 0, 6),
        });
        if (!string.IsNullOrEmpty(current))
        {
            body.Children.Add(new TextBlock
            {
                Text = $"Current key on file: {masked}",
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        body.Children.Add(new TextBlock
        {
            Text = "1. Open these pages (browser)",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 4, 0, 8),
        });

        body.Children.Add(CreateExternalLinkButton(
            "Open Solar API setup guide",
            GoogleSolarSetupGuideUrl,
            "Step-by-step: billing → enable API → create key"));
        body.Children.Add(CreateExternalLinkButton(
            "Enable Solar API in Google Cloud",
            GoogleSolarApiEnableUrl,
            "Opens the Solar API library page — click Enable"));
        body.Children.Add(CreateExternalLinkButton(
            "Enable Geocoding API (for addresses)",
            GoogleGeocodingApiEnableUrl,
            "Needed if you import by street address instead of lat,lon"));
        body.Children.Add(CreateExternalLinkButton(
            "Enable Maps JavaScript API (satellite picker)",
            GoogleMapsJsApiEnableUrl,
            "Needed for smooth Google satellite zoom in the house picker"));
        body.Children.Add(CreateExternalLinkButton(
            "Create / copy API key (Credentials)",
            GoogleCredentialsUrl,
            "Create credentials → API key → copy, then paste below"));

        body.Children.Add(new TextBlock
        {
            Text = "2. Paste API key",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 14, 0, 6),
        });

        var box = new TextBox
        {
            Text = "",
            Padding = new Thickness(8, 8, 8, 8),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 36,
        };
        body.Children.Add(box);

        body.Children.Add(new TextBlock
        {
            Text = "Saved to %LOCALAPPDATA%\\solarSim\\google-api-key.txt",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        root.Children.Add(buttons);
        root.Children.Add(body);
        dialog.Content = root;

        string? result = null;
        save.Click += (_, _) =>
        {
            result = box.Text;
            dialog.DialogResult = true;
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        dialog.Loaded += (_, _) =>
        {
            box.Focus();
            Keyboard.Focus(box);
        };

        if (dialog.ShowDialog() != true || result is null)
            return;

        if (string.IsNullOrWhiteSpace(result))
        {
            MessageBox.Show(this, "Key unchanged (empty input).", "Google API key",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        GoogleSolarApiKeyStore.Save(result);
        MessageBox.Show(this, "API key saved.", "Google API key",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private Button CreateExternalLinkButton(string label, string url, string tooltip)
    {
        var button = new Button
        {
            Content = "↗  " + label,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 9, 12, 9),
            Cursor = Cursors.Hand,
            ToolTip = tooltip + "\n" + url,
            Style = (Style)FindResource("SidebarButton"),
            Background = (Brush)FindResource("AccentSoftBrush"),
            Foreground = (Brush)FindResource("TipFgBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
        };
        button.Click += (_, _) => OpenExternalUrl(url);
        return button;
    }

    private static void OpenExternalUrl(string url) => ExternalLinks.Open(url);

    private static bool IsAllowedExternalUrl(string url) => ExternalLinks.IsAllowed(url);

    private async void ImportGoogleSolar_Click(object sender, RoutedEventArgs e)
    {
        // Optional / advanced only — primary roof import is free map tracing.
        var goFree = MessageBox.Show(this,
            "Recommended: use free Trace roof on map (no Google billing).\n\n"
            + "Continue with optional Google Solar API import anyway?",
            "Google Solar (optional)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (goFree != MessageBoxResult.Yes)
        {
            SatelliteMap_Click(sender, e);
            return;
        }

        var apiKey = EnsureGoogleApiKey();
        if (apiKey is null) return;

        var query = PromptText(
            "Optional Google Solar import",
            "Address or lat,lon:",
            _project.Site.LocationName is "Unspecified" or null or ""
                ? ""
                : _project.Site.LocationName);
        if (query is null) return;

        if (string.IsNullOrWhiteSpace(query)
            && _project.Site.LatitudeDegrees is double slat
            && _project.Site.LongitudeDegrees is double slon)
        {
            query = $"{slat.ToString(System.Globalization.CultureInfo.InvariantCulture)},"
                    + $"{slon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        if (string.IsNullOrWhiteSpace(query))
            return;

        try
        {
            double lat;
            double lon;
            string label = query;
            var client = new GoogleSolarClient(apiKey);
            if (GoogleSolarClient.TryParseLatLon(query, out lat, out lon))
                label = $"{lat:0.####}, {lon:0.####}";
            else
                (lat, lon) = await client.GeocodeAsync(query).ConfigureAwait(true);

            await ImportGoogleSolarAtAsync(apiKey, lat, lon, label).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText.Text = "GOOGLE SOLAR  |  Import failed";
            MessageBox.Show(this, ex.Message, "Google Solar import failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SatelliteMap_Click(object sender, RoutedEventArgs e)
    {
        // Free path: WebView2 + Leaflet/Esri + Nominatim + user-traced roof. No Google billing.
        var initialQuery = _project.Site.LocationName;
        if (string.IsNullOrWhiteSpace(initialQuery)
            || initialQuery.Equals("Unspecified", StringComparison.OrdinalIgnoreCase))
        {
            initialQuery = null;
        }

        double? initLat = _project.Site.LatitudeDegrees;
        double? initLon = _project.Site.LongitudeDegrees;

        var dialog = new SatelliteMapDialog(null, initialQuery, initLat, initLon)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
            return;

        var rings = dialog.RoofRings.Count > 0
            ? dialog.RoofRings
            : dialog.RoofOutline.Count >= 3
                ? new IReadOnlyList<(double Lat, double Lon)>[] { dialog.RoofOutline }
                : Array.Empty<IReadOnlyList<(double Lat, double Lon)>>();
        if (rings.Count == 0)
            return;

        try
        {
            if (_project.Roofs.Roofs.Count > 0)
            {
                var confirm = MessageBox.Show(this,
                    "Import replaces all current roof layers. Continue?",
                    "Trace roof",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
            }

            var import = FreeRoofTraceImport.BuildMany(rings, dialog.SelectedLabel);
            FreeRoofTraceImport.ApplyToProject(_project, import, dialog.SelectedLabel);
            // Map traces are never square — auto-align longest edge + snap edges to H/V.
            try
            {
                _project.History.Execute(new StraightenRoofEdgesCommand(_project));
            }
            catch (InvalidOperationException)
            {
                // No closed roof — ignore.
            }
            UpdateSiteFieldBoxes();
            _workspacePlan = WorkspacePlan.Roof;
            ApplyWorkspacePlanUi();
            // Center the imported roof in the visible canvas (was spawning off-screen / below).
            Dispatcher.BeginInvoke(() =>
            {
                FrameRoofsInView();
                RefreshAll();
            }, DispatcherPriority.Loaded);
            StatusText.Text = $"ROOF  |  {import.Summary}  ·  straightened + locked";
            if (HudDetail is not null)
                HudDetail.Text = import.Summary + "  ·  Squared up & locked — Trace again if wrong; Unlock only to tweak";
        }
        catch (Exception ex)
        {
            StatusText.Text = "ROOF  |  Trace import failed";
            MessageBox.Show(this, ex.Message, "Trace roof failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? EnsureGoogleApiKey()
    {
        var apiKey = GoogleSolarApiKeyStore.TryResolve();
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        var go = MessageBox.Show(this,
            "No Google API key found.\n\n"
            + "Open the setup dialog? It includes one-click links to enable Solar API and create a key — then paste it here.\n\n"
            + "(You can also set env SOLARSIM_GOOGLE_API_KEY.)",
            "Google Solar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (go == MessageBoxResult.Yes)
            ShowGoogleApiKeyDialog();

        apiKey = GoogleSolarApiKeyStore.TryResolve();
        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    private async Task ImportGoogleSolarAtAsync(string apiKey, double lat, double lon, string label)
    {
        if (_project.Roofs.Roofs.Count > 0)
        {
            var confirm = MessageBox.Show(this,
                "Import replaces all current roof layers. Continue?",
                "Google Solar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        StatusText.Text = "GOOGLE SOLAR  |  Requesting building insights…";
        var client = new GoogleSolarClient(apiKey);
        var insights = await client.FindClosestBuildingAsync(lat, lon).ConfigureAwait(true);
        var import = GoogleSolarClient.BuildRoofImport(insights, label);
        GoogleSolarClient.ApplyToProject(_project, import, label);

        UpdateSiteFieldBoxes();
        _workspacePlan = WorkspacePlan.Roof;
        ApplyWorkspacePlanUi();
        RefreshAll();
        StatusText.Text = $"GOOGLE SOLAR  |  {import.Summary}";
        MessageBox.Show(this, import.Summary, "Google Solar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string? PromptText(string title, string message, string defaultValue)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("BgBrush"),
        };
        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var ok = new Button { Content = "Save", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = (Brush)FindResource("TextBrush"),
        });
        var box = new TextBox
        {
            Text = defaultValue,
            Padding = new Thickness(6, 4, 6, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        body.Children.Add(box);
        root.Children.Add(buttons);
        root.Children.Add(body);
        dialog.Content = root;
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };
        cancel.Click += (_, _) => { dialog.DialogResult = false; };
        return dialog.ShowDialog() == true ? result : null;
    }

    private void AddObstacleMode_Click(object sender, RoutedEventArgs e)
    {
        if (!_project.Roofs.HasAnyClosedRoof)
        {
            MessageBox.Show(this, "Create/close a roof before placing obstacles.", "Roof",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _project.Roofs.EnsureActiveRoof();
        _tool = CanvasTool.PlaceObstacle;
        UpdateToolButtonStyles();
        StatusText.Text = "PLACE OBSTACLE  |  Click on the roof to drop a vent  |  Esc cancel";
    }

    private void AddAcDisconnect_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm();
        PlaceAndSelectEquipment(_project.AddAcDisconnect(x, y));
    }

    private void AddBattery_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(1000);
        PlaceAndSelectEquipment(_project.AddBattery(x, y));
    }

    private void AddBattery10kW_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(1000);
        PlaceAndSelectEquipment(_project.AddBattery10kWWall(x, y));
    }

    private void AddBatteryRack_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(1700);
        PlaceAndSelectEquipment(_project.AddBattery5_1kWhRack(x, y));
    }

    private void AddBattery12_8V_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(1500);
        PlaceAndSelectEquipment(_project.AddBattery12_8V300Ah(x, y));
    }

    private void AddBatteryDisconnect_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            BatteryDisconnectGuide.RatingWarning + "\n\n" +
            "Example (DHM1B): 100A ≤6 AWG · 250A ≤1/0 · 400A ≤2/0 · 600A ≤250 MCM.\n" +
            "Other series (DHM1X / DHM3Z) differ — pick series + amps in the inspector.",
            "Check your battery disconnect rating",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        var (x, y) = NextEquipmentPlaceMm(800);
        PlaceAndSelectEquipment(_project.AddBatteryDisconnect(x, y));
    }

    private void AddAcLoadCenter_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = NextEquipmentPlaceMm(1200);
        PlaceAndSelectEquipment(_project.AddAcLoadCenter(x, y));
    }

    private void SingleLine_Click(object sender, RoutedEventArgs e)
    {
        ShowInspectorDump("Single-Line", _project.BuildSingleLineSummary());
    }

    private void BomSchedule_Click(object sender, RoutedEventArgs e)
    {
        // Refresh wire lengths from current canvas geometry first.
        RebuildWireVisuals();
        ShowInspectorDump("BOM / Wire Schedule", _project.BuildBomSchedule().ToPlainText());
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        RebuildWireVisuals();
        var dialog = new SaveFileDialog
        {
            Title = "Export solarSim design report",
            Filter = "HTML report (*.html)|*.html",
            FileName = $"{SanitizeFileName(_project.Name)}_report.html",
            AddExtension = true,
            DefaultExt = ".html",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var path = _project.ExportDesignReportHtml(dialog.FileName);
            InspectorHeading.Text = "DESIGN REPORT";
            InspectorBody.Text =
                $"Report written:\n{path}\n\n" +
                "Opened in your browser.\nCtrl+P → Save as PDF for a printable package.\n\n" +
                "Includes: single-line · array layout · module schedule · racking · BOM.";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export report failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Untitled";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private void ShowAttachmentsCheck_Changed(object sender, RoutedEventArgs e)
    {
        // XAML sets IsChecked during InitializeComponent before DesignCanvas / summary exist.
        if (!IsLoaded || _refreshRunning || DesignCanvas is null || RackingSummaryText is null)
            return;
        _showAttachments = ShowAttachmentsCheck.IsChecked == true;
        RebuildRackingVisuals();
    }

    private void RackingBox_LostFocus(object sender, RoutedEventArgs e) => ApplyRackingFromBoxes();

    private void RackingBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyRackingFromBoxes();
            e.Handled = true;
        }
    }

    private void ApplyRackingFromBoxes()
    {
        if (!double.TryParse(RafterSpacingBox.Text, out var rafter) || rafter < 50 || rafter > 2000)
        {
            UpdateRackingBoxes();
            return;
        }
        if (!double.TryParse(RailOverhangBox.Text, out var overhang) || overhang < 0 || overhang > 1000)
        {
            UpdateRackingBoxes();
            return;
        }
        if (!double.TryParse(AttachmentEdgeBox.Text, out var edge) || edge < 0 || edge > 1000)
        {
            UpdateRackingBoxes();
            return;
        }

        _project.Racking.RafterSpacingMm = rafter;
        _project.Racking.RailOverhangMm = overhang;
        _project.Racking.AttachmentEdgeOffsetMm = edge;
        _project.NotifyChanged("Racking parameters");
        RebuildRackingVisuals();
    }

    private void UpdateRackingBoxes()
    {
        RafterSpacingBox.Text = _project.Racking.RafterSpacingMm.ToString("0.#");
        RailOverhangBox.Text = _project.Racking.RailOverhangMm.ToString("0.#");
        AttachmentEdgeBox.Text = _project.Racking.AttachmentEdgeOffsetMm.ToString("0.#");
        if (ShowAttachmentsCheck.IsChecked != _showAttachments)
            ShowAttachmentsCheck.IsChecked = _showAttachments;
    }

    private void RebuildRackingVisuals()
    {
        if (DesignCanvas is null || RackingSummaryText is null) return;

        foreach (var el in _rackingVisuals)
            DesignCanvas.Children.Remove(el);
        _rackingVisuals.Clear();

        var layout = _project.ComputeRackingLayout();
        if (layout.RailCount == 0)
        {
            RackingSummaryText.Text = "Place modules to estimate rails / attachments.";
            return;
        }

        RackingSummaryText.Text =
            $"{layout.RowCount} row · {layout.RailCount} rails · {layout.TotalRailLengthMm / 1000.0:0.##} m rail · " +
            $"{layout.AttachmentCount} attachments · {layout.EndClampCount} end / {layout.MidClampCount} mid clamps";

        if (!_showAttachments || !ShowsRoofGeometry)
            return;

        var brush = (Brush)FindResource("AttachmentBrush");
        var size = Math.Clamp(7 * _zoom, 5, 12);
        foreach (var pt in layout.AttachmentPoints)
        {
            var (cx, cy) = WorldToCanvas(pt.X, pt.Y);
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = brush,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                IsHitTestVisible = false,
                Opacity = 0.9,
                ToolTip = "Attachment (est.)",
            };
            Canvas.SetLeft(dot, cx - size / 2);
            Canvas.SetTop(dot, cy - size / 2);
            Panel.SetZIndex(dot, 400);
            DesignCanvas.Children.Add(dot);
            _rackingVisuals.Add(dot);
        }
    }

    private void SetbackBox_LostFocus(object sender, RoutedEventArgs e) => ApplySetbackFromBox();

    private void SetbackBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySetbackFromBox();
            e.Handled = true;
        }
    }

    private void SiteField_LostFocus(object sender, RoutedEventArgs e) => ApplySiteFieldsFromBoxes();

    private void SiteField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySiteFieldsFromBoxes();
            e.Handled = true;
        }
    }

    private void ApplySiteFieldsFromBoxes()
    {
        if (LocationNameBox is null || MinAmbientBox is null || HotCellBox is null
            || PeakSunHoursBox is null || DerateBox is null
            || ArrayTiltBox is null || ArrayAzimuthBox is null)
            return;

        if (!double.TryParse(MinAmbientBox.Text, out var cold) || cold < -60 || cold > 40)
        {
            UpdateSiteFieldBoxes();
            return;
        }

        if (!double.TryParse(HotCellBox.Text, out var hot) || hot < 0 || hot > 120)
        {
            UpdateSiteFieldBoxes();
            return;
        }

        if (!double.TryParse(PeakSunHoursBox.Text, out var psh) || psh < 0 || psh > 12)
        {
            UpdateSiteFieldBoxes();
            return;
        }

        if (!double.TryParse(DerateBox.Text, out var derate) || derate < 0.1 || derate > 1.0)
        {
            UpdateSiteFieldBoxes();
            return;
        }

        if (!double.TryParse(ArrayTiltBox.Text, out var tilt) || tilt < 0 || tilt > 60)
        {
            UpdateSiteFieldBoxes();
            return;
        }

        if (!double.TryParse(ArrayAzimuthBox.Text, out var az) || az < 0 || az > 360)
        {
            UpdateSiteFieldBoxes();
            return;
        }

        double? lat = null;
        double? lon = null;
        if (!string.IsNullOrWhiteSpace(LatitudeBox.Text))
        {
            if (!double.TryParse(LatitudeBox.Text, out var latVal) || latVal < -90 || latVal > 90)
            {
                UpdateSiteFieldBoxes();
                return;
            }
            lat = latVal;
        }

        if (!string.IsNullOrWhiteSpace(LongitudeBox.Text))
        {
            if (!double.TryParse(LongitudeBox.Text, out var lonVal) || lonVal < -180 || lonVal > 180)
            {
                UpdateSiteFieldBoxes();
                return;
            }
            lon = lonVal;
        }

        var location = string.IsNullOrWhiteSpace(LocationNameBox.Text) ? "Unspecified" : LocationNameBox.Text.Trim();

        var changed =
            !string.Equals(_project.Site.LocationName, location, StringComparison.Ordinal)
            || _project.Site.LatitudeDegrees != lat
            || _project.Site.LongitudeDegrees != lon
            || Math.Abs(_project.Site.MinAmbientCelsius - cold) >= 0.001
            || Math.Abs(_project.Site.HotCellCelsius - hot) >= 0.001
            || Math.Abs(_project.Site.PeakSunHoursPerDay - psh) >= 0.001
            || Math.Abs(_project.Site.SystemDerateFactor - derate) >= 0.001
            || Math.Abs(_project.Site.ArrayTiltDegrees - tilt) >= 0.001
            || Math.Abs(_project.Site.ArrayAzimuthDegrees - az) >= 0.001;

        if (!changed) return;

        _project.Site.LocationName = location;
        _project.Site.LatitudeDegrees = lat;
        _project.Site.LongitudeDegrees = lon;
        _project.Site.MinAmbientCelsius = cold;
        _project.Site.HotCellCelsius = hot;
        _project.Site.PeakSunHoursPerDay = psh;
        _project.Site.SystemDerateFactor = derate;
        _project.Site.ArrayTiltDegrees = tilt;
        _project.Site.ArrayAzimuthDegrees = az;
        _project.NotifyChanged("Change site assumptions");
        RefreshStatusAndInspector();
    }

    private void UpdateSiteTempBoxes() => UpdateSiteFieldBoxes();

    private void UpdateSiteFieldBoxes()
    {
        if (LocationNameBox is null || MinAmbientBox is null || HotCellBox is null
            || PeakSunHoursBox is null || DerateBox is null || LatitudeBox is null || LongitudeBox is null
            || ArrayTiltBox is null || ArrayAzimuthBox is null)
            return;

        if (LocationNameBox.IsKeyboardFocusWithin || MinAmbientBox.IsKeyboardFocusWithin
            || HotCellBox.IsKeyboardFocusWithin || PeakSunHoursBox.IsKeyboardFocusWithin
            || DerateBox.IsKeyboardFocusWithin || LatitudeBox.IsKeyboardFocusWithin
            || LongitudeBox.IsKeyboardFocusWithin || ArrayTiltBox.IsKeyboardFocusWithin
            || ArrayAzimuthBox.IsKeyboardFocusWithin)
            return;

        LocationNameBox.Text = _project.Site.LocationName;
        LatitudeBox.Text = _project.Site.LatitudeDegrees?.ToString("0.###") ?? "";
        LongitudeBox.Text = _project.Site.LongitudeDegrees?.ToString("0.###") ?? "";
        MinAmbientBox.Text = _project.Site.MinAmbientCelsius.ToString("0.#");
        HotCellBox.Text = _project.Site.HotCellCelsius.ToString("0.#");
        PeakSunHoursBox.Text = _project.Site.PeakSunHoursPerDay.ToString("0.#");
        DerateBox.Text = _project.Site.SystemDerateFactor.ToString("0.##");
        ArrayTiltBox.Text = _project.Site.ArrayTiltDegrees.ToString("0.#");
        ArrayAzimuthBox.Text = _project.Site.ArrayAzimuthDegrees.ToString("0.#");
    }

    private bool _climatePresetUiReady;

    private void PopulateClimatePresetCombo()
    {
        if (ClimatePresetCombo is null) return;
        ClimatePresetCombo.Items.Clear();
        ClimatePresetCombo.Items.Add(new ComboBoxItem { Content = "— Climate preset —", Tag = null });
        foreach (var preset in SiteClimatePresets.All)
        {
            ClimatePresetCombo.Items.Add(new ComboBoxItem
            {
                Content = preset.DisplayName,
                Tag = preset.Id,
            });
        }
        ClimatePresetCombo.SelectedIndex = 0;
        _climatePresetUiReady = true;
    }

    private void ClimatePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_climatePresetUiReady) return;
        if (ClimatePresetCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string id)
            return;
        var preset = SiteClimatePresets.Find(id);
        if (preset is null) return;

        _project.Site.ApplyPreset(preset);
        _project.NotifyChanged($"Apply climate preset: {preset.DisplayName}");
        UpdateSiteFieldBoxes();
        RefreshStatusAndInspector();
    }

    private void PvlibStatus_Click(object sender, RoutedEventArgs e)
    {
        var status = PvlibProductionBridge.Probe();
        var csharp = _project.GetDetailedProductionEstimate();
        InspectorHeading.Text = "PVLIB STATUS";
        InspectorBody.Text =
            $"{status.Summary}\n\n" +
            $"Script: {status.ScriptPath ?? "(missing)"}\n" +
            $"Python: {status.PythonPath ?? "(missing)"}\n\n" +
            "Built-in C# estimate stays the default for Single-Line / reports.\n" +
            $"C# annual ~{csharp.EstimatedAnnualKwh:0} kWh/yr\n\n" +
            (_project.LastPvlibEstimate is { } last
                ? $"Last pvlib run ~{last.EstimatedAnnualKwh:0} kWh/yr\n{last.MethodNote}"
                : "No pvlib run yet.");
        MessageBox.Show(this, status.Summary, "pvlib status", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RunPvlibEstimate_Click(object sender, RoutedEventArgs e)
    {
        ApplySiteFieldsFromBoxes();
        StatusText.Text = "PVLIB  |  Running optional yield estimate…";
        try
        {
            var kw = _project.GetCalculationSnapshot().TotalPmaxWatts / 1000.0;
            var result = await PvlibProductionBridge.EstimateAsync(kw, _project.Site).ConfigureAwait(true);
            if (!result.Ok || result.Estimate is null)
            {
                var csharp = _project.GetDetailedProductionEstimate();
                _project.SetLastPvlibEstimate(null, result.Error);
                InspectorHeading.Text = "PVLIB (UNAVAILABLE)";
                InspectorBody.Text =
                    $"{result.Error}\n\n" +
                    "Using built-in C# monthly estimate instead:\n" +
                    $"  ~{csharp.EstimatedAnnualKwh:0} kWh/yr\n" +
                    string.Join("\n", csharp.Months.Select(m => $"  {m.MonthName}: {m.EstimatedKwh:0} kWh"));
                StatusText.Text = "PVLIB  |  Unavailable — using C# estimate";
                MessageBox.Show(this,
                    result.Error + "\n\nBuilt-in C# estimate remains available.",
                    "pvlib",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _project.SetLastPvlibEstimate(result.Estimate, "ok");
            var e2 = result.Estimate;
            InspectorHeading.Text = "PVLIB YIELD";
            InspectorBody.Text =
                $"Engine: {result.Engine}\n" +
                $"Annual ~{e2.EstimatedAnnualKwh:0} kWh  ({e2.EstimatedDailyKwh:0.##} kWh/d)\n" +
                $"Array {e2.ArrayKwDc:0.###} kW · tilt {e2.ArrayTiltDegrees:0.#}° · az {e2.ArrayAzimuthDegrees:0.#}°\n\n" +
                string.Join("\n", e2.Months.Select(m => $"{m.MonthName}: {m.EstimatedKwh:0} kWh")) +
                $"\n\n{e2.MethodNote}";
            StatusText.Text = $"PVLIB  |  ~{e2.EstimatedAnnualKwh:0} kWh/yr";
        }
        catch (Exception ex)
        {
            StatusText.Text = "PVLIB  |  Failed";
            MessageBox.Show(this, ex.Message, "pvlib failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplySetbackFromBox()
    {
        if (GetActiveRoofSurface() is not { } active)
            return;

        if (!double.TryParse(SetbackBox.Text, out var value) || value < 0)
        {
            UpdateSetbackBoxFromActiveRoof();
            return;
        }

        active.SetbackMm = DisplayUnitToMm(value);
        _project.NotifyChanged("Change setback");
        RefreshAll();
    }

    private void UpdateToolButtonStyles()
    {
        DrawRoofButton.FontWeight = _tool == CanvasTool.DrawRoof ? FontWeights.Bold : FontWeights.Normal;
        AddObstacleButton.FontWeight = _tool == CanvasTool.PlaceObstacle ? FontWeights.Bold : FontWeights.Normal;
    }

    private void RebuildRoofVisuals()
    {
        foreach (var element in _roofVisuals)
            DesignCanvas.Children.Remove(element);
        _roofVisuals.Clear();

        if (!ShowsRoofGeometry)
            return;

        var activeId = _project.Roofs.ActiveRoofId;
        foreach (var roof in _project.Roofs.Roofs)
        {
            if (!roof.IsVisible) continue;
            BuildRoofSurfaceVisual(roof, roof.Id == activeId);
        }
    }

    private void AddRoofRotateHandle(RoofSurface roof)
    {
        var all = _project.Roofs.Roofs.Where(r => r.HasRoof).SelectMany(r => r.Vertices).ToList();
        if (all.Count == 0) all = roof.Vertices.ToList();

        var minX = all.Min(v => v.X);
        var maxX = all.Max(v => v.X);
        var maxY = all.Max(v => v.Y);
        var midX = (minX + maxX) * 0.5;

        // World Y-up → canvas Y-down: maxY is the bottom edge on screen.
        var (cx, bottomY) = WorldToCanvas(midX, maxY);
        var handleSize = Math.Clamp(28 * Math.Min(_zoom, 1.15), 26, 34);
        var gap = Math.Max(14, 18 * Math.Min(_zoom, 1.2));
        var handleY = bottomY + gap;

        _roofRotateHandle = CreateCanvaRotateHandle(
            handleSize,
            "Drag to rotate · snaps to 90° / 45° / 15° · Shift = hard 15° · Alt = free");
        Canvas.SetLeft(_roofRotateHandle, cx - handleSize / 2);
        Canvas.SetTop(_roofRotateHandle, handleY);
        Panel.SetZIndex(_roofRotateHandle, 1410);
        _roofRotateHandle.Cursor = Cursors.Hand;
        _roofRotateHandle.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            BeginRoofRotation(e.GetPosition(DesignCanvas));
            e.Handled = true;
        };
        DesignCanvas.Children.Add(_roofRotateHandle);
        _roofVisuals.Add(_roofRotateHandle);
    }

    private void BeginRoofRotation(Point canvasPos)
    {
        var roofs = _project.Roofs.Roofs.Where(r => r.HasRoof).ToList();
        if (roofs.Count == 0) return;

        _roofRotateBaseline.Clear();
        foreach (var roof in roofs)
            _roofRotateBaseline[roof.Id] = roof.Vertices.ToList();

        var all = _roofRotateBaseline.Values.SelectMany(v => v).ToList();
        _roofRotatePivot = RoofGeometry.Centroid(all);
        var (cx, cy) = WorldToCanvas(_roofRotatePivot.X, _roofRotatePivot.Y);
        _roofRotateStartMouseDeg = Math.Atan2(canvasPos.Y - cy, canvasPos.X - cx) * (180.0 / Math.PI);
        _roofRotateLiveDegrees = 0;
        _rotatingRoof = true;
        _draggingRoofBody = false;
        _rotateMoved = false;
        DesignCanvas.CaptureMouse();
        UpdateRotateDegreeLabel(new Point(cx, cy), 0);
        StatusText.Text = "ROOF  |  Drag to rotate · snaps at 90° / 45° / 15° (Alt = free)";
    }

    private void BeginRoofBodyDrag(Point canvasPos)
    {
        var roofs = _project.Roofs.Roofs.Where(r => r.HasRoof && !r.IsLocked).ToList();
        if (roofs.Count == 0) return;

        _roofRotateBaseline.Clear();
        foreach (var roof in roofs)
            _roofRotateBaseline[roof.Id] = roof.Vertices.ToList();

        _roofDragStartCanvas = canvasPos;
        _roofDragDxMm = 0;
        _roofDragDyMm = 0;
        _draggingRoofBody = true;
        _rotatingRoof = false;
        _rotateMoved = false;
        DesignCanvas.CaptureMouse();
        StatusText.Text = "ROOF  |  Moving (Alt+drag)";
    }

    /// <summary>
    /// Free rotation with magnetic snap: strongest at 90°, then 45°, then 15°.
    /// Shift = hard 15° grid. Alt = fully free (no magnet).
    /// </summary>
    private static double SnapRoofDragDegrees(double delta)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            return delta;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return Math.Round(delta / 15.0) * 15.0;

        // Prefer stronger magnets (checked in order).
        if (TryMagnet(delta, step: 90, radius: 12, out var ortho))
            return ortho;
        if (TryMagnet(delta, step: 45, radius: 7, out var fortyFive))
            return fortyFive;
        if (TryMagnet(delta, step: 15, radius: 4, out var fifteen))
            return fifteen;

        return delta;
    }

    private static bool TryMagnet(double delta, double step, double radius, out double snapped)
    {
        snapped = Math.Round(delta / step) * step;
        return Math.Abs(delta - snapped) <= radius;
    }

    private void ApplyLiveRoofRotation(double degrees)
    {
        foreach (var (id, before) in _roofRotateBaseline)
        {
            var roof = _project.Roofs.Find(id);
            if (roof is null) continue;
            roof.SetVertices(RoofGeometry.RotateVertices(before, _roofRotatePivot, degrees), closed: true);
        }

        _roofRotateLiveDegrees = degrees;
        RebuildRoofVisuals();
        var (cx, cy) = WorldToCanvas(_roofRotatePivot.X, _roofRotatePivot.Y);
        UpdateRotateDegreeLabel(new Point(cx, cy), degrees);
    }

    private void ApplyLiveRoofTranslate(double dxMm, double dyMm)
    {
        foreach (var (id, before) in _roofRotateBaseline)
        {
            var roof = _project.Roofs.Find(id);
            if (roof is null) continue;
            roof.SetVertices(RoofGeometry.TranslateVertices(before, dxMm, dyMm), closed: true);
        }

        _roofDragDxMm = dxMm;
        _roofDragDyMm = dyMm;
        RebuildRoofVisuals();
    }

    private void BuildRoofSurfaceVisual(RoofSurface roof, bool isActive)
    {
        var vertices = roof.Vertices;
        if (vertices.Count == 0) return;

        var strokeBrush = isActive
            ? new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F))
            : new SolidColorBrush(Color.FromRgb(0x45, 0x5A, 0x64));
        var strokeThickness = isActive ? 3 : 2;

        var points = new PointCollection();
        foreach (var v in vertices)
        {
            var (x, y) = WorldToCanvas(v.X, v.Y);
            points.Add(new Point(x, y));
        }

        if (roof.IsClosed && vertices.Count >= 3)
        {
            var fill = new System.Windows.Shapes.Polygon
            {
                Points = points,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(isActive ? 36 : 24), 0x90, 0xA4, 0xAE)),
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                IsHitTestVisible = false,
                Cursor = Cursors.SizeAll,
            };
            DesignCanvas.Children.Insert(0, fill);
            _roofVisuals.Add(fill);

            if (isActive && roof.SetbackMm > 0)
            {
                var inset = RoofGeometry.InsetConvexPolygon(vertices, roof.SetbackMm);
                if (inset.Count >= 3)
                {
                    var insetPoints = new PointCollection();
                    var valid = true;
                    foreach (var p in inset)
                    {
                        if (double.IsNaN(p.X) || double.IsNaN(p.Y)
                            || double.IsInfinity(p.X) || double.IsInfinity(p.Y)
                            || Math.Abs(p.X) > 1e9 || Math.Abs(p.Y) > 1e9)
                        {
                            valid = false;
                            break;
                        }
                        var (ix, iy) = WorldToCanvas(p.X, p.Y);
                        insetPoints.Add(new Point(ix, iy));
                    }

                    if (valid && insetPoints.Count >= 3)
                    {
                        var setbackPoly = new System.Windows.Shapes.Polygon
                        {
                            Points = insetPoints,
                            Fill = Brushes.Transparent,
                            Stroke = new SolidColorBrush(Color.FromArgb(180, 0xF5, 0x9E, 0x0B)),
                            StrokeThickness = 1.5,
                            StrokeDashArray = new DoubleCollection { 6, 4 },
                            IsHitTestVisible = false,
                        };
                        DesignCanvas.Children.Insert(1, setbackPoly);
                        _roofVisuals.Add(setbackPoly);
                    }
                }
            }

            foreach (var (a, b, lengthMm) in roof.EdgeMeasurements())
            {
                var (ax, ay) = WorldToCanvas(a.X, a.Y);
                var (bx, by) = WorldToCanvas(b.X, b.Y);
                var label = new TextBlock
                {
                    Text = _project.Units.FormatLength(lengthMm),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)),
                    Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    Padding = new Thickness(3, 1, 3, 1),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(label, (ax + bx) / 2);
                Canvas.SetTop(label, (ay + by) / 2 - 10);
                DesignCanvas.Children.Add(label);
                _roofVisuals.Add(label);
            }

            var areaLabel = new TextBlock
            {
                Text = $"{roof.Name} {_project.Units.FormatAreaSquareMeters(roof.AreaSquareMeters())}",
                FontSize = 12,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)),
                IsHitTestVisible = false,
            };
            var cx = vertices.Average(v => v.X);
            var cy = vertices.Average(v => v.Y);
            var (acx, acy) = WorldToCanvas(cx, cy);
            Canvas.SetLeft(areaLabel, acx - 40);
            Canvas.SetTop(areaLabel, acy - 8);
            DesignCanvas.Children.Add(areaLabel);
            _roofVisuals.Add(areaLabel);
        }
        else
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var line = new Line
                {
                    X1 = points[i].X,
                    Y1 = points[i].Y,
                    X2 = points[i + 1].X,
                    Y2 = points[i + 1].Y,
                    Stroke = strokeBrush,
                    StrokeThickness = strokeThickness,
                    IsHitTestVisible = false,
                };
                DesignCanvas.Children.Insert(0, line);
                _roofVisuals.Add(line);
            }
        }

        if (isActive && !roof.IsLocked)
        {
            for (var i = 0; i < vertices.Count; i++)
            {
                var (x, y) = WorldToCanvas(vertices[i].X, vertices[i].Y);
                // Shrink handles when zoomed in so edges stay readable under the tip.
                var handleSize = Math.Clamp(8.0 / Math.Sqrt(Math.Max(_zoom, 0.4)), 4.5, 8);
                var handle = new Ellipse
                {
                    Width = handleSize,
                    Height = handleSize,
                    Fill = Brushes.White,
                    Stroke = (Brush)FindResource("AccentBrush"),
                    StrokeThickness = 1.5,
                    Tag = i,
                    Cursor = Cursors.Arrow,
                };
                Canvas.SetLeft(handle, x - handleSize / 2);
                Canvas.SetTop(handle, y - handleSize / 2);
                DesignCanvas.Children.Add(handle);
                _roofVisuals.Add(handle);
            }
        }

        // Rotate handle always available on closed roofs (lock only blocks move/vertex edits).
        if (isActive && roof.IsClosed && vertices.Count >= 3)
            AddRoofRotateHandle(roof);

        foreach (var obstacle in roof.Obstacles)
        {
            var (x, y) = WorldToCanvas(obstacle.XMm, obstacle.YMm);
            var w = obstacle.WidthMm * MmToPx * _zoom;
            var h = obstacle.HeightMm * MmToPx * _zoom;
            var rect = new Border
            {
                Width = w,
                Height = h,
                Background = new SolidColorBrush(Color.FromArgb(120, 0xBF, 0x36, 0x0C)),
                BorderBrush = _selectedObstacleId == obstacle.Id
                    ? (Brush)FindResource("AccentBrush")
                    : new SolidColorBrush(Color.FromRgb(0xBF, 0x36, 0x0C)),
                BorderThickness = new Thickness(_selectedObstacleId == obstacle.Id ? 3 : 1.5),
                CornerRadius = new CornerRadius(2),
                Child = new TextBlock
                {
                    Text = obstacle.Label,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                },
                Tag = obstacle.Id,
                Cursor = Cursors.Arrow,
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            DesignCanvas.Children.Add(rect);
            _roofVisuals.Add(rect);
        }
    }

    private int? FindRoofVertexAt(Point canvasPoint)
    {
        var active = GetActiveRoofSurface();
        if (active is null) return null;

        for (var i = 0; i < active.Vertices.Count; i++)
        {
            var (x, y) = WorldToCanvas(active.Vertices[i].X, active.Vertices[i].Y);
            if (Hypot(canvasPoint.X - x, canvasPoint.Y - y) <= 10)
                return i;
        }
        return null;
    }

    private Guid? FindClosedRoofAt(Point canvasPoint)
    {
        var (wx, wy) = CanvasToWorld(canvasPoint);
        var world = new Point2Mm(wx, wy);
        // Prefer active roof, then others (top-most / last wins for overlap).
        foreach (var roof in _project.Roofs.Roofs.Reverse())
        {
            if (!roof.IsVisible || !roof.IsClosed || roof.Vertices.Count < 3) continue;
            if (RoofGeometry.IsPointInsidePolygon(world, roof.Vertices))
                return roof.Id;
        }
        return null;
    }

    private Guid? FindObstacleAt(Point canvasPoint)
    {
        var active = GetActiveRoofSurface();
        if (active is null) return null;

        foreach (var obstacle in active.Obstacles.Reverse())
        {
            var (x, y) = WorldToCanvas(obstacle.XMm, obstacle.YMm);
            var w = obstacle.WidthMm * MmToPx * _zoom;
            var h = obstacle.HeightMm * MmToPx * _zoom;
            if (canvasPoint.X >= x && canvasPoint.X <= x + w &&
                canvasPoint.Y >= y && canvasPoint.Y <= y + h)
                return obstacle.Id;
        }
        return null;
    }

    private RoofSurface? GetActiveRoofSurface() => _project.Roofs.ActiveRoof;

    private bool HasAnyRoofVertices() =>
        _project.Roofs.Roofs.Any(r => r.Vertices.Count > 0);

    private double GetActiveRoofSetbackMm() =>
        GetActiveRoofSurface()?.SetbackMm ?? 457.2;

    private bool TryCloseActiveRoof()
    {
        var active = GetActiveRoofSurface();
        return active is not null && active.TryClose();
    }

    private const double RoofCloseSnapPx = 28;
    private const double RoofAxisSnapPx = 22;

    private Point2Mm? _roofAlignXSource;
    private Point2Mm? _roofAlignYSource;
    private Ellipse? _roofAlignMarker;

    private Point2Mm ResolveRoofDrawPoint(
        RoofSurface roof,
        Point canvasPos,
        out bool closing,
        out string? levelHint)
    {
        closing = false;
        levelHint = null;
        _roofAlignXSource = null;
        _roofAlignYSource = null;
        var (xMm, yMm) = CanvasToWorld(canvasPos);
        var raw = new Point2Mm(xMm, yMm);

        if (roof.Vertices.Count >= 3)
        {
            var first = roof.Vertices[0];
            var (fx, fy) = WorldToCanvas(first.X, first.Y);
            if (Hypot(canvasPos.X - fx, canvasPos.Y - fy) <= RoofCloseSnapPx)
            {
                closing = true;
                levelHint = GetOrthoLevelHint(roof.Vertices[^1], first);
                _roofAlignXSource = first;
                _roofAlignYSource = first;
                return first;
            }
        }

        var snapped = SnapRoofPoint(roof, raw, out var alignX, out var alignY);
        _roofAlignXSource = alignX;
        _roofAlignYSource = alignY;
        if (roof.Vertices.Count > 0)
            levelHint = GetOrthoLevelHint(roof.Vertices[^1], snapped);
        return snapped;
    }

    private static string? GetOrthoLevelHint(Point2Mm from, Point2Mm to)
    {
        const double tolMm = 0.5;
        if (Math.Abs(from.Y - to.Y) <= tolMm) return "LEVEL";
        if (Math.Abs(from.X - to.X) <= tolMm) return "PLUMB";
        return null;
    }

    private Point2Mm SnapRoofPoint(
        RoofSurface roof,
        Point2Mm raw,
        out Point2Mm? alignX,
        out Point2Mm? alignY)
    {
        alignX = null;
        alignY = null;
        if (roof.Vertices.Count == 0)
            return raw;

        var free = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        var axisTolMm = RoofAxisSnapPx / (MmToPx * Math.Max(_zoom, 0.05));
        return RoofGeometry.SnapDrawPoint(
            roof.Vertices[^1],
            raw,
            roof.Vertices,
            axisTolMm,
            free,
            out alignX,
            out alignY);
    }

    /// <summary>
    /// Subtle dashed guides while dragging a roof corner (LEVEL / PLUMB / ALIGN).
    /// </summary>
    private void UpdateRoofEditSnapGuides(RoofSurface roof, int index, Point2Mm snapped)
    {
        var n = roof.Vertices.Count;
        if (n < 2) return;
        var prev = roof.Vertices[(index - 1 + n) % n];
        var (x1, y1) = WorldToCanvas(prev.X, prev.Y);
        var (x2, y2) = WorldToCanvas(snapped.X, snapped.Y);
        var levelHint = GetOrthoLevelHint(prev, snapped);
        // Also show PLUMB/LEVEL if the edge toward next is ortho.
        var next = roof.Vertices[(index + 1) % n];
        levelHint ??= GetOrthoLevelHint(snapped, next);
        UpdateRoofLeveler(x1, y1, x2, y2, levelHint, snapped);
    }

    private void ClearRoofLiveMeasure()
    {
        void Remove(UIElement? el)
        {
            if (el is not null) DesignCanvas.Children.Remove(el);
        }

        Remove(_roofRubberBandLine);
        _roofRubberBandLine = null;
        Remove(_roofLiveMeasureLabel);
        _roofLiveMeasureLabel = null;
        Remove(_roofCloseMarker);
        _roofCloseMarker = null;
        Remove(_roofCloseLabel);
        _roofCloseLabel = null;
        Remove(_roofLevelBadge);
        _roofLevelBadge = null;
        Remove(_roofLevelGuideH);
        _roofLevelGuideH = null;
        Remove(_roofLevelGuideV);
        _roofLevelGuideV = null;
        Remove(_roofAlignMarker);
        _roofAlignMarker = null;
        _roofAlignXSource = null;
        _roofAlignYSource = null;
    }

    private void ClearMeasureTool()
    {
        ClearMeasureRubberBand();
        foreach (var el in _measureVisuals)
            DesignCanvas.Children.Remove(el);
        _measureVisuals.Clear();
        _measurePoints.Clear();
    }

    private void ClearMeasureRubberBand()
    {
        if (_measureRubberBand is not null)
        {
            DesignCanvas.Children.Remove(_measureRubberBand);
            _measureRubberBand = null;
        }
        if (_measureLiveLabel is not null)
        {
            DesignCanvas.Children.Remove(_measureLiveLabel);
            _measureLiveLabel = null;
        }
    }

    private void RebuildMeasureVisuals()
    {
        ClearMeasureRubberBand();
        foreach (var el in _measureVisuals)
            DesignCanvas.Children.Remove(el);
        _measureVisuals.Clear();

        for (var i = 0; i < _measurePoints.Count; i++)
        {
            var (cx, cy) = WorldToCanvas(_measurePoints[i].X, _measurePoints[i].Y);
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = (Brush)FindResource("AccentBrush"),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(dot, cx - 4);
            Canvas.SetTop(dot, cy - 4);
            Panel.SetZIndex(dot, 1500);
            DesignCanvas.Children.Add(dot);
            _measureVisuals.Add(dot);

            if (i == 0) continue;
            var prev = _measurePoints[i - 1];
            var (ax, ay) = WorldToCanvas(prev.X, prev.Y);
            var line = new Line
            {
                X1 = ax, Y1 = ay, X2 = cx, Y2 = cy,
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            Panel.SetZIndex(line, 1490);
            DesignCanvas.Children.Add(line);
            _measureVisuals.Add(line);

            var lenMm = prev.DistanceTo(_measurePoints[i]);
            var label = new TextBlock
            {
                Text = _project.Units.FormatLength(lenMm),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                Padding = new Thickness(4, 2, 4, 2),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, (ax + cx) / 2);
            Canvas.SetTop(label, (ay + cy) / 2 - 14);
            Panel.SetZIndex(label, 1500);
            DesignCanvas.Children.Add(label);
            _measureVisuals.Add(label);
        }

        if (_measurePoints.Count >= 2)
        {
            var total = 0.0;
            for (var i = 1; i < _measurePoints.Count; i++)
                total += _measurePoints[i - 1].DistanceTo(_measurePoints[i]);
            StatusText.Text = $"MEASURE  ·  {_measurePoints.Count - 1} segment(s) · {_project.Units.FormatLength(total)} total";
        }
        else
        {
            StatusText.Text = "MEASURE  ·  Click next point (Esc clears)";
        }
    }

    private void UpdateMeasureRubberBand(Point canvasPos)
    {
        if (_measurePoints.Count == 0) return;
        var last = _measurePoints[^1];
        var (x1, y1) = WorldToCanvas(last.X, last.Y);
        var (xMm, yMm) = CanvasToWorld(canvasPos);
        var cur = new Point2Mm(xMm, yMm);
        var (x2, y2) = WorldToCanvas(cur.X, cur.Y);
        var lenMm = last.DistanceTo(cur);

        if (_measureRubberBand is null)
        {
            _measureRubberBand = new Line
            {
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_measureRubberBand);
            Panel.SetZIndex(_measureRubberBand, 1500);
        }
        _measureRubberBand.X1 = x1;
        _measureRubberBand.Y1 = y1;
        _measureRubberBand.X2 = x2;
        _measureRubberBand.Y2 = y2;

        if (_measureLiveLabel is null)
        {
            _measureLiveLabel = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                Padding = new Thickness(4, 2, 4, 2),
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_measureLiveLabel);
            Panel.SetZIndex(_measureLiveLabel, 1500);
        }
        _measureLiveLabel.Text = _project.Units.FormatLength(lenMm);
        Canvas.SetLeft(_measureLiveLabel, (x1 + x2) / 2);
        Canvas.SetTop(_measureLiveLabel, (y1 + y2) / 2 - 14);
    }

    private void UpdateRoofLiveMeasure(Point canvasPos, RoofSurface roof)
    {
        var last = roof.Vertices[^1];
        var snapped = ResolveRoofDrawPoint(roof, canvasPos, out var closing, out var levelHint);
        var lengthMm = last.DistanceTo(snapped);

        if (_roofRubberBandLine is null)
        {
            _roofRubberBandLine = new Line
            {
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_roofRubberBandLine);
            Panel.SetZIndex(_roofRubberBandLine, 1500);
        }

        var (x1, y1) = WorldToCanvas(last.X, last.Y);
        var (x2, y2) = WorldToCanvas(snapped.X, snapped.Y);
        _roofRubberBandLine.X1 = x1;
        _roofRubberBandLine.Y1 = y1;
        _roofRubberBandLine.X2 = x2;
        _roofRubberBandLine.Y2 = y2;
        _roofRubberBandLine.Stroke = closing
            ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
            : (Brush)FindResource("AccentBrush");

        if (_roofLiveMeasureLabel is null)
        {
            _roofLiveMeasureLabel = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                Padding = new Thickness(4, 2, 4, 2),
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_roofLiveMeasureLabel);
            Panel.SetZIndex(_roofLiveMeasureLabel, 1500);
        }

        var alignNote = (_roofAlignXSource is not null || _roofAlignYSource is not null) ? "  ·  ALIGN" : "";
        _roofLiveMeasureLabel.Text = closing
            ? $"CLOSE  ·  {_project.Units.FormatLength(lengthMm)}"
            : _project.Units.FormatLength(lengthMm) + alignNote;
        Canvas.SetLeft(_roofLiveMeasureLabel, (x1 + x2) / 2);
        Canvas.SetTop(_roofLiveMeasureLabel, (y1 + y2) / 2 - 14);

        UpdateRoofCloseCue(roof, closing);
        UpdateRoofLeveler(x1, y1, x2, y2, levelHint, snapped);
    }

    private void UpdateRoofCloseCue(RoofSurface roof, bool closing)
    {
        if (roof.Vertices.Count < 3)
        {
            if (_roofCloseMarker is not null)
            {
                DesignCanvas.Children.Remove(_roofCloseMarker);
                _roofCloseMarker = null;
            }
            if (_roofCloseLabel is not null)
            {
                DesignCanvas.Children.Remove(_roofCloseLabel);
                _roofCloseLabel = null;
            }
            return;
        }

        var first = roof.Vertices[0];
        var (fx, fy) = WorldToCanvas(first.X, first.Y);
        var size = (closing ? 12.0 : 8.0) / Math.Sqrt(Math.Max(_zoom, 0.45));
        size = Math.Clamp(size, 5, 14);

        if (_roofCloseMarker is null)
        {
            _roofCloseMarker = new Ellipse
            {
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_roofCloseMarker);
            Panel.SetZIndex(_roofCloseMarker, 1500);
        }

        _roofCloseMarker.Width = size;
        _roofCloseMarker.Height = size;
        _roofCloseMarker.Stroke = closing
            ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
            : (Brush)FindResource("AccentBrush");
        _roofCloseMarker.Fill = closing
            ? new SolidColorBrush(Color.FromArgb(60, 0x2E, 0x7D, 0x32))
            : new SolidColorBrush(Color.FromArgb(40, 0xF5, 0x9E, 0x0B));
        Canvas.SetLeft(_roofCloseMarker, fx - size / 2);
        Canvas.SetTop(_roofCloseMarker, fy - size / 2);

        if (closing)
        {
            if (_roofCloseLabel is null)
            {
                _roofCloseLabel = new TextBlock
                {
                    Text = "SNAP TO CLOSE",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20)),
                    Background = new SolidColorBrush(Color.FromArgb(230, 232, 245, 233)),
                    Padding = new Thickness(6, 3, 6, 3),
                    IsHitTestVisible = false,
                };
                DesignCanvas.Children.Add(_roofCloseLabel);
                Panel.SetZIndex(_roofCloseLabel, 1500);
            }

            Canvas.SetLeft(_roofCloseLabel, fx + size / 2 + 6);
            Canvas.SetTop(_roofCloseLabel, fy - 10);
        }
        else if (_roofCloseLabel is not null)
        {
            DesignCanvas.Children.Remove(_roofCloseLabel);
            _roofCloseLabel = null;
        }
    }

    private void UpdateRoofLeveler(
        double x1, double y1, double x2, double y2,
        string? levelHint,
        Point2Mm snapped)
    {
        var alignX = _roofAlignXSource;
        var alignY = _roofAlignYSource;
        var hasAlign = alignX is not null || alignY is not null;
        var hasOrtho = levelHint is not null;

        if (!hasAlign && !hasOrtho)
        {
            if (_roofLevelBadge is not null)
            {
                DesignCanvas.Children.Remove(_roofLevelBadge);
                _roofLevelBadge = null;
            }
            if (_roofLevelGuideH is not null)
            {
                DesignCanvas.Children.Remove(_roofLevelGuideH);
                _roofLevelGuideH = null;
            }
            if (_roofLevelGuideV is not null)
            {
                DesignCanvas.Children.Remove(_roofLevelGuideV);
                _roofLevelGuideV = null;
            }
            if (_roofAlignMarker is not null)
            {
                DesignCanvas.Children.Remove(_roofAlignMarker);
                _roofAlignMarker = null;
            }
            return;
        }

        var guideColor = new SolidColorBrush(Color.FromArgb(180, 0xF5, 0x9E, 0x0B));
        var orthoColor = new SolidColorBrush(Color.FromArgb(160, 0x2E, 0x7D, 0x32));

        // Vertical alignment guide (same X as an earlier corner — even left/right sides).
        if (alignX is Point2Mm ax)
        {
            var (sx, sy) = WorldToCanvas(ax.X, ax.Y);
            var (cx, cy) = WorldToCanvas(snapped.X, snapped.Y);
            if (_roofLevelGuideV is null)
            {
                _roofLevelGuideV = new Line
                {
                    StrokeThickness = 1.25,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    IsHitTestVisible = false,
                };
                DesignCanvas.Children.Add(_roofLevelGuideV);
                Panel.SetZIndex(_roofLevelGuideV, 1400);
            }

            _roofLevelGuideV.Stroke = guideColor;
            _roofLevelGuideV.X1 = sx;
            _roofLevelGuideV.X2 = sx;
            _roofLevelGuideV.Y1 = Math.Min(sy, cy) - 48;
            _roofLevelGuideV.Y2 = Math.Max(sy, cy) + 48;

            if (_roofAlignMarker is null)
            {
                _roofAlignMarker = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Stroke = (Brush)FindResource("AccentBrush"),
                    StrokeThickness = 2,
                    Fill = Brushes.White,
                    IsHitTestVisible = false,
                };
                DesignCanvas.Children.Add(_roofAlignMarker);
                Panel.SetZIndex(_roofAlignMarker, 1500);
            }

            Canvas.SetLeft(_roofAlignMarker, sx - 6);
            Canvas.SetTop(_roofAlignMarker, sy - 6);
        }
        else if (_roofLevelGuideV is not null && levelHint != "PLUMB")
        {
            DesignCanvas.Children.Remove(_roofLevelGuideV);
            _roofLevelGuideV = null;
        }

        // Horizontal alignment guide (same Y as an earlier corner — even top/bottom).
        if (alignY is Point2Mm ay)
        {
            var (sx, sy) = WorldToCanvas(ay.X, ay.Y);
            var (cx, cy) = WorldToCanvas(snapped.X, snapped.Y);
            if (_roofLevelGuideH is null)
            {
                _roofLevelGuideH = new Line
                {
                    StrokeThickness = 1.25,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    IsHitTestVisible = false,
                };
                DesignCanvas.Children.Add(_roofLevelGuideH);
                Panel.SetZIndex(_roofLevelGuideH, 1400);
            }

            _roofLevelGuideH.Stroke = guideColor;
            _roofLevelGuideH.X1 = Math.Min(sx, cx) - 48;
            _roofLevelGuideH.X2 = Math.Max(sx, cx) + 48;
            _roofLevelGuideH.Y1 = sy;
            _roofLevelGuideH.Y2 = sy;
        }
        else if (_roofLevelGuideH is not null && levelHint != "LEVEL")
        {
            DesignCanvas.Children.Remove(_roofLevelGuideH);
            _roofLevelGuideH = null;
        }

        // Ortho-only guides when not already showing alignment guides on that axis.
        if (levelHint == "LEVEL" && alignY is null)
        {
            if (_roofLevelGuideH is null)
            {
                _roofLevelGuideH = new Line
                {
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 4 },
                    IsHitTestVisible = false,
                };
                DesignCanvas.Children.Add(_roofLevelGuideH);
                Panel.SetZIndex(_roofLevelGuideH, 1400);
            }

            _roofLevelGuideH.Stroke = orthoColor;
            _roofLevelGuideH.X1 = Math.Min(x1, x2) - 40;
            _roofLevelGuideH.X2 = Math.Max(x1, x2) + 40;
            _roofLevelGuideH.Y1 = y1;
            _roofLevelGuideH.Y2 = y1;
        }
        else if (levelHint == "PLUMB" && alignX is null)
        {
            if (_roofLevelGuideV is null)
            {
                _roofLevelGuideV = new Line
                {
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 4 },
                    IsHitTestVisible = false,
                };
                DesignCanvas.Children.Add(_roofLevelGuideV);
                Panel.SetZIndex(_roofLevelGuideV, 1400);
            }

            _roofLevelGuideV.Stroke = orthoColor;
            _roofLevelGuideV.X1 = x1;
            _roofLevelGuideV.X2 = x1;
            _roofLevelGuideV.Y1 = Math.Min(y1, y2) - 40;
            _roofLevelGuideV.Y2 = Math.Max(y1, y2) + 40;
        }

        if (alignX is null && _roofAlignMarker is not null)
        {
            DesignCanvas.Children.Remove(_roofAlignMarker);
            _roofAlignMarker = null;
        }

        if (_roofLevelBadge is null)
        {
            _roofLevelBadge = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20)),
                Background = new SolidColorBrush(Color.FromArgb(230, 232, 245, 233)),
                Padding = new Thickness(6, 3, 6, 3),
                IsHitTestVisible = false,
            };
            DesignCanvas.Children.Add(_roofLevelBadge);
            Panel.SetZIndex(_roofLevelBadge, 1500);
        }

        if (hasAlign)
        {
            _roofLevelBadge.Text = alignX is not null && alignY is not null
                ? "ALIGN  X+Y"
                : alignX is not null ? "ALIGN  ┃  even sides" : "ALIGN  ━━  even sides";
            _roofLevelBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x4F, 0xC2));
            _roofLevelBadge.Background = new SolidColorBrush(Color.FromArgb(230, 232, 240, 255));
        }
        else
        {
            _roofLevelBadge.Text = levelHint == "LEVEL" ? "━━  LEVEL" : "┃  PLUMB";
            _roofLevelBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
            _roofLevelBadge.Background = new SolidColorBrush(Color.FromArgb(230, 232, 245, 233));
        }

        Canvas.SetLeft(_roofLevelBadge, (x1 + x2) / 2 - 40);
        Canvas.SetTop(_roofLevelBadge, (y1 + y2) / 2 + 10);
    }

    private static RoofSurface CloneRoofSurface(RoofSurface source)
    {
        var roof = new RoofSurface(source.Id, source.Name)
        {
            IsVisible = source.IsVisible,
            IsLocked = source.IsLocked,
            SetbackMm = source.SetbackMm,
            EnforceSetback = source.EnforceSetback,
            EnforceBoundary = source.EnforceBoundary,
            EnforceObstacles = source.EnforceObstacles,
        };
        roof.SetVertices(source.Vertices, source.IsClosed);
        foreach (var obstacle in source.Obstacles)
        {
            roof.AddObstacle(new RoofObstacle(
                obstacle.Id,
                obstacle.Kind,
                obstacle.XMm,
                obstacle.YMm,
                obstacle.WidthMm,
                obstacle.HeightMm,
                obstacle.Label,
                obstacle.AllowOverlap));
        }
        return roof;
    }

    private void PopulateUnitsCombo()
    {
        _unitsComboUiReady = false;
        UnitsCombo.Items.Clear();
        foreach (UnitConversionService.LengthDisplayUnit unit in Enum.GetValues<UnitConversionService.LengthDisplayUnit>())
        {
            UnitsCombo.Items.Add(new ComboBoxItem
            {
                Content = UnitConversionService.UnitLabel(unit),
                Tag = unit,
            });
        }
        SelectCurrentUnitInCombo();
        _unitsComboUiReady = true;
    }

    private bool _panelColorComboUiReady;

    private void PopulatePanelColorCombo()
    {
        _panelColorComboUiReady = false;
        PanelColorCombo.Items.Clear();
        foreach (PanelAppearance.Kind kind in Enum.GetValues<PanelAppearance.Kind>())
        {
            PanelColorCombo.Items.Add(new ComboBoxItem
            {
                Content = PanelAppearance.DisplayName(kind),
                Tag = kind,
            });
        }

        for (var i = 0; i < PanelColorCombo.Items.Count; i++)
        {
            if (PanelColorCombo.Items[i] is ComboBoxItem item
                && item.Tag is PanelAppearance.Kind kind
                && kind == PanelAppearance.Current)
            {
                PanelColorCombo.SelectedIndex = i;
                break;
            }
        }

        _panelColorComboUiReady = true;
    }

    private void PanelColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_panelColorComboUiReady) return;
        if (PanelColorCombo.SelectedItem is not ComboBoxItem { Tag: PanelAppearance.Kind kind }) return;
        if (kind == PanelAppearance.Current) return;
        PanelAppearance.Apply(kind);
        RefreshAll();
    }

    private bool _unitsComboUiReady;

    private void SelectCurrentUnitInCombo()
    {
        for (var i = 0; i < UnitsCombo.Items.Count; i++)
        {
            if (UnitsCombo.Items[i] is ComboBoxItem item
                && item.Tag is UnitConversionService.LengthDisplayUnit unit
                && unit == _project.Units.PreferredLengthUnit)
            {
                UnitsCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private void UnitsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_unitsComboUiReady) return;
        if (UnitsCombo.SelectedItem is not ComboBoxItem item
            || item.Tag is not UnitConversionService.LengthDisplayUnit unit)
            return;

        if (_project.Units.PreferredLengthUnit == unit)
        {
            SetbackLabel.Text = $"Setback ({UnitConversionService.UnitLabel(unit)})";
            UpdateSetbackBoxFromActiveRoof();
            return;
        }

        _project.Units.PreferredLengthUnit = unit;
        SetbackLabel.Text = $"Setback ({UnitConversionService.UnitLabel(unit)})";
        UpdateSetbackBoxFromActiveRoof();
        RefreshAll();
    }

    private void UpdateSetbackBoxFromActiveRoof()
    {
        SetbackLabel.Text = $"Setback ({UnitConversionService.UnitLabel(_project.Units.PreferredLengthUnit)})";
        if (GetActiveRoofSurface() is { } active)
            SetbackBox.Text = MmToDisplayUnit(active.SetbackMm).ToString("0.###");
        else
            SetbackBox.Text = MmToDisplayUnit(457.2).ToString("0.###");
    }

    private double MmToDisplayUnit(double mm)
    {
        switch (_project.Units.PreferredLengthUnit)
        {
            case UnitConversionService.LengthDisplayUnit.Millimeters:
                return mm;
            case UnitConversionService.LengthDisplayUnit.Meters:
                return _project.Units.MmToMeters(mm);
            case UnitConversionService.LengthDisplayUnit.Feet:
                return _project.Units.MmToFeet(mm);
            case UnitConversionService.LengthDisplayUnit.FeetInches:
                return _project.Units.MmToInches(mm);
            case UnitConversionService.LengthDisplayUnit.Yards:
                return _project.Units.MmToYards(mm);
            case UnitConversionService.LengthDisplayUnit.Inches:
                return _project.Units.MmToInches(mm);
            default:
                return mm;
        }
    }

    private double DisplayUnitToMm(double value)
    {
        switch (_project.Units.PreferredLengthUnit)
        {
            case UnitConversionService.LengthDisplayUnit.Millimeters:
                return value;
            case UnitConversionService.LengthDisplayUnit.Meters:
                return _project.Units.MetersToMm(value);
            case UnitConversionService.LengthDisplayUnit.Feet:
                return _project.Units.InchesToMm(value * 12.0);
            case UnitConversionService.LengthDisplayUnit.FeetInches:
                return _project.Units.InchesToMm(value);
            case UnitConversionService.LengthDisplayUnit.Yards:
                return _project.Units.InchesToMm(value * 36.0);
            case UnitConversionService.LengthDisplayUnit.Inches:
                return _project.Units.InchesToMm(value);
            default:
                return value;
        }
    }

    private void RefreshLayersPanel()
    {
        _suppressLayerListSelection = true;
        LayerList.Items.Clear();

        switch (_layersCategory)
        {
            case LayersCategory.Roofs:
                foreach (var roof in _project.Roofs.Roofs)
                {
                    var prefix = roof.Id == _project.Roofs.ActiveRoofId ? "* " : "  ";
                    var hidden = roof.IsVisible ? "" : " (hidden)";
                    LayerList.Items.Add(new RoofLayerItem(
                        roof.Id,
                        $"{prefix}{roof.Name}{hidden}",
                        roof.Id == _project.Roofs.ActiveRoofId));
                }
                break;
            case LayersCategory.Panels:
                foreach (var panel in _project.Graph.Panels.Values.OrderBy(p => p.PositionYMm).ThenBy(p => p.PositionXMm))
                {
                    if (!_project.Definitions.TryGetValue(panel.DefinitionId, out var def)) continue;
                    LayerList.Items.Add(new PanelLayerItem(panel.Id, $"{def.DisplayName}  {def.PmaxWatts:0} W"));
                }
                break;
            case LayersCategory.Equipment:
                foreach (var eq in _project.Graph.Equipment.Values.OrderBy(e => e.Name))
                    LayerList.Items.Add(new EquipmentLayerItem(eq.Id, $"{eq.Name}  ({eq.Kind})"));
                break;
        }

        SelectLayerListItemForCurrentSelection();
        _suppressLayerListSelection = false;
    }

    private void SelectLayerListItemForCurrentSelection()
    {
        if (_layersCategory == LayersCategory.Roofs && _project.Roofs.ActiveRoofId is Guid activeRoofId)
        {
            for (var i = 0; i < LayerList.Items.Count; i++)
            {
                if (LayerList.Items[i] is RoofLayerItem item && item.RoofId == activeRoofId)
                {
                    LayerList.SelectedIndex = i;
                    return;
                }
            }
        }
        else if (_layersCategory == LayersCategory.Panels && _selectedPanelIds.Count == 1)
        {
            var panelId = _selectedPanelIds.First();
            for (var i = 0; i < LayerList.Items.Count; i++)
            {
                if (LayerList.Items[i] is PanelLayerItem item && item.PanelId == panelId)
                {
                    LayerList.SelectedIndex = i;
                    return;
                }
            }
        }
        else if (_layersCategory == LayersCategory.Equipment && _selectedEquipmentIds.Count == 1)
        {
            var equipmentId = _selectedEquipmentIds.First();
            for (var i = 0; i < LayerList.Items.Count; i++)
            {
                if (LayerList.Items[i] is EquipmentLayerItem item && item.EquipmentId == equipmentId)
                {
                    LayerList.SelectedIndex = i;
                    return;
                }
            }
        }
    }

    private void LayersRoofsTab_Click(object sender, RoutedEventArgs e)
    {
        _layersCategory = LayersCategory.Roofs;
        UpdateLayersTabStyles();
        RefreshLayersPanel();
    }

    private void LayersPanelsTab_Click(object sender, RoutedEventArgs e)
    {
        _layersCategory = LayersCategory.Panels;
        UpdateLayersTabStyles();
        RefreshLayersPanel();
    }

    private void LayersEquipmentTab_Click(object sender, RoutedEventArgs e)
    {
        _layersCategory = LayersCategory.Equipment;
        UpdateLayersTabStyles();
        RefreshLayersPanel();
    }

    private void UpdateLayersTabStyles()
    {
        StyleLayerTab(LayersRoofsTab, _layersCategory == LayersCategory.Roofs);
        StyleLayerTab(LayersPanelsTab, _layersCategory == LayersCategory.Panels);
        StyleLayerTab(LayersEquipmentTab, _layersCategory == LayersCategory.Equipment);
    }

    private void StyleLayerTab(Button button, bool active)
    {
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        button.Background = active
            ? (Brush)FindResource("AccentBrush")
            : Brushes.Transparent;
        button.Foreground = active
            ? Brushes.White
            : (Brush)FindResource("MutedBrush");
    }

    private void LayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLayerListSelection) return;

        switch (LayerList.SelectedItem)
        {
            case RoofLayerItem roofItem:
                _project.Roofs.SetActive(roofItem.RoofId);
                _selectedPanelIds.Clear();
                _selectedConnectionIds.Clear();
                _selectedEquipmentIds.Clear();
                _selectedObstacleId = null;
                RefreshAll();
                break;
            case PanelLayerItem panelItem:
                SetSelection(panels: new[] { panelItem.PanelId });
                break;
            case EquipmentLayerItem equipmentItem:
                _selectedPanelIds.Clear();
                _selectedConnectionIds.Clear();
                _selectedEquipmentIds.Clear();
                _selectedEquipmentIds.Add(equipmentItem.EquipmentId);
                _selectedObstacleId = null;
                RefreshAll();
                break;
        }
    }

    private void LayerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LayerList.SelectedItem is RoofLayerItem roofItem)
        {
            _project.Roofs.SetActive(roofItem.RoofId);
            _project.NotifyChanged("Set active roof");
            RefreshAll();
        }
    }

    private void DeleteRoofLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_layersCategory != LayersCategory.Roofs) return;

        Guid? roofId = LayerList.SelectedItem is RoofLayerItem item
            ? item.RoofId
            : _project.Roofs.ActiveRoofId;

        if (roofId is not Guid id || _project.Roofs.Find(id) is null) return;

        var result = MessageBox.Show(this,
            "Delete the selected roof layer?",
            "Delete roof",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _project.Roofs.RemoveRoof(id);
        _selectedObstacleId = null;
        _project.NotifyChanged("Delete roof layer");
        RefreshAll();
    }

    private sealed class WireCanvasVisual
    {
        public Guid ConnectionId { get; init; }
        public List<UIElement> Shapes { get; } = new();
        public List<Point> HitPoints { get; set; } = new();
        public List<Ellipse> Handles { get; } = new();

        public void RemoveFrom(Canvas canvas)
        {
            foreach (var shape in Shapes)
                canvas.Children.Remove(shape);
            foreach (var h in Handles)
                canvas.Children.Remove(h);
        }
    }

    private sealed class EquipmentVisual
    {
        public Guid InstanceId { get; }
        public Canvas Root { get; }
        public Border Body { get; }
        public TextBlock Title { get; }
        public Dictionary<Guid, Ellipse> PortEllipses { get; }
        public Ellipse RotateHandle { get; }
        public Line RotateStem { get; }
        public RotateTransform RotateTransform { get; }

        public EquipmentVisual(
            Guid instanceId,
            Canvas root,
            Border body,
            TextBlock title,
            Dictionary<Guid, Ellipse> portEllipses,
            Ellipse rotateHandle,
            Line rotateStem,
            RotateTransform rotateTransform)
        {
            InstanceId = instanceId;
            Root = root;
            Body = body;
            Title = title;
            PortEllipses = portEllipses;
            RotateHandle = rotateHandle;
            RotateStem = rotateStem;
            RotateTransform = rotateTransform;
        }
    }

    private sealed class PanelVisual
    {
        public Guid InstanceId { get; }
        public Canvas Root { get; }
        public Border Body { get; }
        public TextBlock PowerLabel { get; }
        public TextBlock Label { get; }
        public Ellipse PositivePort { get; }
        public Ellipse NegativePort { get; }
        public TextBlock PositiveLabel { get; }
        public TextBlock NegativeLabel { get; }
        public Border RotateHandle { get; }
        public Line RotateStem { get; }

        public PanelVisual(
            Guid instanceId,
            Canvas root,
            Border body,
            TextBlock powerLabel,
            TextBlock label,
            Ellipse positive,
            Ellipse negative,
            TextBlock positiveLabel,
            TextBlock negativeLabel,
            Border rotateHandle,
            Line rotateStem)
        {
            InstanceId = instanceId;
            Root = root;
            Body = body;
            PowerLabel = powerLabel;
            Label = label;
            PositivePort = positive;
            NegativePort = negative;
            PositiveLabel = positiveLabel;
            NegativeLabel = negativeLabel;
            RotateHandle = rotateHandle;
            RotateStem = rotateStem;
        }
    }

    private sealed record StringListItem(Guid StringId, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record RoofLayerItem(Guid RoofId, string Display, bool IsActive)
    {
        public override string ToString() => Display;
    }

    private sealed record PanelLayerItem(Guid PanelId, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record EquipmentLayerItem(Guid EquipmentId, string Display)
    {
        public override string ToString() => Display;
    }
}
