using System;
using System.IO;
using System.Linq;
using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Unity.Canvas;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SolarSim.Unity.UI
{
    /// <summary>
    /// Wires UI Toolkit chrome to the headless SolarProject document.
    /// Canvas interaction is handled by <see cref="DesignCanvasController"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(-100)]
    public sealed class AppShellController : MonoBehaviour
    {
        [SerializeField] private UIDocument? uiDocument;

        private SolarProject _project = null!;
        private Label? _statusText;
        private Label? _projectName;
        private Label? _inspectorHeading;
        private Label? _inspectorBody;
        private VisualElement? _emptyState;
        private ScrollView? _stringsList;
        private Guid? _selectedPanelId;
        private bool _uiBound;

        public SolarProject Project => _project;

        private void Awake()
        {
            uiDocument ??= GetComponent<UIDocument>();
            EnsurePanelSettings(uiDocument);
            _project = new SolarProject();
        }

        private static void EnsurePanelSettings(UIDocument doc)
        {
            if (doc == null || doc.panelSettings != null) return;
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.sortingOrder = 100;
            doc.panelSettings = settings;
            Debug.LogWarning("AppShell UIDocument had no Panel Settings — created a runtime fallback.");
        }

        private void OnEnable()
        {
            _project.ProjectChanged += OnProjectChanged;
            _project.CalculationsUpdated += RefreshChrome;
            // Visual tree may not be ready yet — bind in Start + on geometry.
            var root = uiDocument!.rootVisualElement;
            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            TryBindUi();
            RefreshChrome();
        }

        private void Start() => TryBindUi();

        private void OnDisable()
        {
            if (_project != null)
            {
                _project.ProjectChanged -= OnProjectChanged;
                _project.CalculationsUpdated -= RefreshChrome;
            }

            if (uiDocument != null)
                uiDocument.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        }

        private void OnRootGeometryChanged(GeometryChangedEvent _) => TryBindUi();

        private void OnProjectChanged(string _) => RefreshChrome();

        private void TryBindUi()
        {
            if (_uiBound || uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root.Q("app-root") == null) return;

            ConfigurePicking(root);

            _statusText = root.Q<Label>("status-text");
            _projectName = root.Q<Label>("project-name");
            _emptyState = root.Q<VisualElement>("empty-state");
            _inspectorHeading = root.Q<Label>("inspector-heading");
            _inspectorBody = root.Q<Label>("inspector-body");
            _stringsList = root.Q<ScrollView>("strings-list");

            BindButton(root, "lib-boviet-270", () => AddBuiltIn(SolarPanelDefinition.CreateBoviet270().Id));
            BindButton(root, "lib-generic-400", () => AddBuiltIn(SolarPanelDefinition.CreateGeneric400().Id));
            BindButton(root, "lib-generic-550", () => AddBuiltIn(SolarPanelDefinition.CreateGeneric550().Id));
            BindButton(root, "btn-add-panel", () => AddBuiltIn(SolarPanelDefinition.CreateBoviet270().Id));
            BindButton(root, "btn-undo", () =>
            {
                _project.History.Undo();
                RefreshChrome();
                FindFirstObjectByType<DesignCanvasController>()?.RebuildAll();
            });
            BindButton(root, "btn-redo", () =>
            {
                _project.History.Redo();
                RefreshChrome();
                FindFirstObjectByType<DesignCanvasController>()?.RebuildAll();
            });
            BindButton(root, "btn-save", SaveProject);
            BindButton(root, "btn-open", OpenProject);

            _uiBound = true;
            Debug.Log("solarSim UI bound — library buttons are live.");
            RefreshChrome();
        }

        private static void BindButton(VisualElement root, string name, Action action)
        {
            var button = root.Q<Button>(name);
            if (button == null)
            {
                Debug.LogWarning($"solarSim UI: missing button '{name}'");
                return;
            }

            button.clicked += action;
        }

        private static void ConfigurePicking(VisualElement root)
        {
            var host = root.Q("canvas-host");
            if (host != null) host.pickingMode = PickingMode.Ignore;
            var empty = root.Q("empty-state");
            if (empty != null) empty.pickingMode = PickingMode.Position;
            foreach (var child in empty?.Children() ?? Enumerable.Empty<VisualElement>())
                child.pickingMode = PickingMode.Position;
        }

        public void SetSelection(Guid? panelId)
        {
            _selectedPanelId = panelId;
            RefreshChrome();
        }

        private void AddBuiltIn(Guid definitionId)
        {
            try
            {
                var count = _project.Graph.Panels.Count;
                var panel = _project.AddPanelFromDefinition(definitionId, count * 1200f, 0f);
                Debug.Log($"Added panel {panel.Id} at ({panel.PositionXMm:0},{panel.PositionYMm:0}) mm — total {_project.Graph.Panels.Count}");
                RefreshChrome();
                var canvas = FindFirstObjectByType<DesignCanvasController>();
                if (canvas == null)
                    Debug.LogError("DesignCanvasController missing — panel is in the model but not drawn.");
                else
                    canvas.RebuildAll();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Add panel failed: {ex}");
            }
        }

        private void SaveProject()
        {
            var path = AskSavePath();
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                SolarProjectSerializer.SaveToFile(_project, path);
                RefreshChrome();
                Debug.Log($"Saved {_project.FilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Save failed: {ex.Message}");
            }
        }

        private void OpenProject()
        {
            var path = AskOpenPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var loaded = SolarProjectSerializer.LoadFromFile(path);
                ReplaceProject(loaded);
                Debug.Log($"Opened {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Open failed: {ex.Message}");
            }
        }

        private void ReplaceProject(SolarProject loaded)
        {
            _project.ProjectChanged -= OnProjectChanged;
            _project.CalculationsUpdated -= RefreshChrome;
            _project = loaded;
            _selectedPanelId = null;
            _project.ProjectChanged += OnProjectChanged;
            _project.CalculationsUpdated += RefreshChrome;
            FindFirstObjectByType<DesignCanvasController>()?.BindToProject();
            RefreshChrome();
        }

        private static string? AskSavePath()
        {
#if UNITY_EDITOR
            return EditorUtility.SaveFilePanel("Save solarSim project", UnityEngine.Application.dataPath, "Untitled", "solarproj");
#else
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "solarSim");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"Design_{DateTime.Now:yyyyMMdd_HHmmss}.solarproj");
#endif
        }

        private static string? AskOpenPath()
        {
#if UNITY_EDITOR
            return EditorUtility.OpenFilePanel("Open solarSim project", UnityEngine.Application.dataPath, "solarproj");
#else
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "solarSim");
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir, "*.solarproj")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
#endif
        }

        private void RefreshChrome()
        {
            if (_projectName != null)
            {
                var file = string.IsNullOrEmpty(_project.FilePath)
                    ? $"{_project.Name}.solarproj"
                    : Path.GetFileName(_project.FilePath);
                _projectName.text = file;
            }

            var calc = _project.GetCalculationSnapshot();
            if (_statusText != null)
            {
                var kw = calc.TotalPmaxWatts >= 1000
                    ? $"{calc.TotalPmaxWatts / 1000.0:0.##} kW DC"
                    : $"{calc.TotalPmaxWatts:0.##} W DC";

                var warningCount = calc.Warnings.Count(w => w.Severity != IssueSeverity.Info);
                var errorCount = calc.Errors.Count;

                if (calc.Strings.Count == 1)
                {
                    var s = calc.Strings[0];
                    _statusText.text =
                        $"{calc.TotalPanels} Panels  |  {kw}  |  {s.DisplayName}: " +
                        $"{s.PanelCount} mod  {s.TotalPmaxWatts:0.##} W  " +
                        $"Vmp {s.VmpVolts:0.##} V  Voc {s.VocVolts:0.##} V  " +
                        $"Imp {s.ImpAmps:0.##} A  Isc {s.IscAmps:0.##} A  |  " +
                        $"{errorCount} Errors  |  {warningCount} Warnings";
                }
                else
                {
                    _statusText.text =
                        $"{calc.TotalPanels} Panels  |  {kw}  |  {calc.StringCount} Strings  |  " +
                        $"{errorCount} Errors  |  {warningCount} Warnings";
                }
            }

            if (_emptyState != null)
                _emptyState.style.display = calc.TotalPanels == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            RefreshInspector(calc);
            RefreshStringsList(calc);
        }

        private void RefreshInspector(ProjectCalculationResult calc)
        {
            if (_inspectorHeading == null || _inspectorBody == null) return;

            if (_selectedPanelId is Guid id && _project.Graph.TryGetPanel(id, out var panel))
            {
                var def = _project.RequireDefinition(panel.DefinitionId);
                _inspectorHeading.text = def.DisplayName;
                _inspectorBody.text =
                    $"Pmax {def.PmaxWatts:0.#} W\n" +
                    $"Vmp {def.VmpVolts:0.##} V · Imp {def.ImpAmps:0.##} A\n" +
                    $"Voc {def.VocVolts:0.##} V · Isc {def.IscAmps:0.##} A\n" +
                    $"Size {def.WidthMm:0} × {def.HeightMm:0} mm\n" +
                    $"Rotation {panel.RotationDegrees}°\n\n" +
                    "Drag PV+ → PV− to series-string.\n" +
                    "Ctrl+D duplicate · R rotate · Del delete";
                return;
            }

            _inspectorHeading.text = "PROJECT";
            _inspectorBody.text = calc.TotalPanels == 0
                ? "Add a module from the library, then string PV+ to PV−."
                : $"{calc.TotalPanels} modules · {calc.StringCount} strings · {calc.TotalPmaxWatts:0.#} W DC";
        }

        private void RefreshStringsList(ProjectCalculationResult calc)
        {
            if (_stringsList == null) return;
            _stringsList.Clear();
            foreach (var s in calc.Strings)
            {
                _stringsList.Add(new Label(
                    $"{s.DisplayName}: {s.PanelCount} mod · {s.TotalPmaxWatts:0.#} W · Vmp {s.VmpVolts:0.##} V"));
            }
            if (calc.Strings.Count == 0)
                _stringsList.Add(new Label("No strings yet.") { style = { color = new StyleColor(new Color(0.4f, 0.4f, 0.4f)) } });
        }
    }
}
