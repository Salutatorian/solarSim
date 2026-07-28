using System;
using System.Collections.Generic;
using System.Linq;
using SolarSim.Application.Commands;
using SolarSim.Application.Project;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Unity.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace SolarSim.Unity.Canvas
{
    /// <summary>
    /// Orthographic design canvas: panel visuals, drag/snap, PV+/PV− wiring.
    /// Electrical truth lives in <see cref="SolarProject"/> — this only presents it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class DesignCanvasController : MonoBehaviour
    {
        private const float SnapThresholdMm = 40f;
        private const float PanelSpacingMm = 20f;
        private const float PortHitRadiusPx = 18f;

        [SerializeField] private AppShellController? appShell;
        [SerializeField] private Camera? designCamera;
        [SerializeField] private UIDocument? uiDocument;

        private readonly Dictionary<Guid, SolarPanelView> _panelViews = new();
        private readonly Dictionary<Guid, WireView> _wireViews = new();
        private Transform _panelsRoot = null!;
        private Transform _wiresRoot = null!;
        private SolarProject? _boundProject;

        private Guid? _selectedPanelId;
        private Guid? _draggingPanelId;
        private Vector2 _dragOriginMm;
        private Vector3 _dragGrabOffsetWorld;
        private bool _dragMoved;

        private Guid? _wireFromPortId;
        private LineRenderer? _previewWire;

        private SolarProject Project => appShell!.Project;

        private void Awake()
        {
            appShell ??= FindFirstObjectByType<AppShellController>();
            designCamera ??= Camera.main;
            if (designCamera == null)
            {
                var camGo = GameObject.Find("DesignCamera");
                if (camGo != null) designCamera = camGo.GetComponent<Camera>();
            }
            uiDocument ??= GetComponent<UIDocument>()
                           ?? (appShell != null ? appShell.GetComponent<UIDocument>() : null);

            _panelsRoot = new GameObject("Panels").transform;
            _panelsRoot.SetParent(transform, false);
            _wiresRoot = new GameObject("Wires").transform;
            _wiresRoot.SetParent(transform, false);
        }

        private void OnEnable() => BindToProject();

        private void OnDisable()
        {
            if (_boundProject != null)
                _boundProject.ProjectChanged -= OnProjectChanged;
            _boundProject = null;
        }

        public void BindToProject()
        {
            if (_boundProject != null)
                _boundProject.ProjectChanged -= OnProjectChanged;
            appShell ??= FindFirstObjectByType<AppShellController>();
            _boundProject = appShell != null ? appShell.Project : null;
            if (_boundProject != null)
                _boundProject.ProjectChanged += OnProjectChanged;
            if (_boundProject != null)
                RebuildAll();
        }

        private void OnProjectChanged(string _) => RebuildAll();

        private void Update()
        {
            if (appShell == null) return;
            designCamera ??= Camera.main;
            if (designCamera == null) return;
            HandleKeyboard();

            if (IsPointerOverChrome()) return;

            if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.Space))
                BeginPointer();
            else if (Input.GetMouseButton(0) && _draggingPanelId is not null)
                DragPanel();
            else if (Input.GetMouseButton(0) && _wireFromPortId is not null)
                DragWire();
            else if (Input.GetMouseButtonUp(0))
                EndPointer();

            UpdatePortVisibility();
        }

        private void HandleKeyboard()
        {
            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Z) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                Project.History.Undo();
                return;
            }
            if (ctrl && (Input.GetKeyDown(KeyCode.Y) || (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))))
            {
                Project.History.Redo();
                return;
            }
            if (ctrl && Input.GetKeyDown(KeyCode.D) && _selectedPanelId is Guid dupId)
            {
                Project.History.Execute(new DuplicatePanelCommand(Project, dupId));
                return;
            }
            if (Input.GetKeyDown(KeyCode.R) && _selectedPanelId is Guid rotId)
            {
                var panel = Project.Graph.GetPanel(rotId);
                Project.History.Execute(new RotatePanelCommand(Project, rotId, panel.RotationDegrees, panel.RotationDegrees + 90));
                return;
            }
            if (Input.GetKeyDown(KeyCode.Delete) && _selectedPanelId is Guid delId)
            {
                Project.History.Execute(new DeletePanelCommand(Project, delId));
                _selectedPanelId = null;
            }
        }

        private bool IsPointerOverChrome()
        {
            if (uiDocument == null || uiDocument.rootVisualElement?.panel == null)
                return false;

            var panel = uiDocument.rootVisualElement.panel;
            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, Input.mousePosition);
            // UI Toolkit Y is flipped relative to Input.mousePosition.
            panelPos.y = panel.visualTree.layout.height - panelPos.y;
            var picked = panel.Pick(panelPos);
            if (picked == null) return false;

            // Allow clicks through the transparent canvas host / empty state backdrop.
            var name = picked.name;
            if (name is "canvas-host" or "empty-state" or "app-root" or "main-row")
                return false;
            if (picked.ClassListContains("empty-title") || picked.ClassListContains("empty-body"))
                return false;

            return true;
        }

        private void BeginPointer()
        {
            var world = ScreenToWorld(Input.mousePosition);

            if (TryHitPort(world, out var portId, out _))
            {
                _wireFromPortId = portId;
                EnsurePreviewWire();
                _draggingPanelId = null;
                return;
            }

            var hit = Physics2D.OverlapPoint(world);
            var view = hit != null ? hit.GetComponent<SolarPanelView>() : null;
            if (view != null)
            {
                _selectedPanelId = view.InstanceId;
                _draggingPanelId = view.InstanceId;
                var panel = Project.Graph.GetPanel(view.InstanceId);
                _dragOriginMm = new Vector2((float)panel.PositionXMm, (float)panel.PositionYMm);
                _dragGrabOffsetWorld = view.transform.position - world;
                _dragMoved = false;
                appShell?.SetSelection(view.InstanceId);
                RebuildAll();
                return;
            }

            _selectedPanelId = null;
            appShell?.SetSelection(null);
            RebuildAll();
        }

        private void DragPanel()
        {
            if (_draggingPanelId is not Guid id || designCamera == null) return;
            var world = ScreenToWorld(Input.mousePosition) + _dragGrabOffsetWorld;
            var mm = WorldScale.WorldToMm(world);
            var panel = Project.Graph.GetPanel(id);
            var def = Project.RequireDefinition(panel.DefinitionId);
            var size = WorldScale.PanelSizeMeters(def, panel.RotationDegrees);
            var widthMm = size.x * WorldScale.MmPerMeter;
            var heightMm = size.y * WorldScale.MmPerMeter;

            // Convert center grab back to min-corner.
            var x = mm.x - widthMm / 2f;
            var y = mm.y - heightMm / 2f;

            if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
                (x, y) = ApplySnap(id, x, y, widthMm, heightMm);

            if (Mathf.Abs(x - _dragOriginMm.x) + Mathf.Abs(y - _dragOriginMm.y) > 1f)
                _dragMoved = true;

            panel.SetPosition(x, y);
            if (_panelViews.TryGetValue(id, out var view))
                view.ApplyTransform(panel, def);
            RebuildWiresOnly();
        }

        private void DragWire()
        {
            if (_wireFromPortId is not Guid fromId || _previewWire == null) return;
            var fromPort = Project.Graph.GetPort(fromId);
            var fromPos = GetPortWorld(fromPort);
            var world = ScreenToWorld(Input.mousePosition);
            var color = fromPort.Polarity == Polarity.Positive
                ? new Color(0.83f, 0.18f, 0.18f)
                : new Color(0.13f, 0.13f, 0.13f);
            _previewWire.startColor = _previewWire.endColor = color;

            if (TryFindCompatiblePort(world, fromId, out var target, out var snapPos))
            {
                _previewWire.SetPosition(0, fromPos);
                _previewWire.SetPosition(1, snapPos);
                _previewWire.startWidth = _previewWire.endWidth = 0.04f;
            }
            else
            {
                _previewWire.SetPosition(0, fromPos);
                _previewWire.SetPosition(1, world);
                _previewWire.startWidth = _previewWire.endWidth = 0.025f;
            }
        }

        private void EndPointer()
        {
            if (_wireFromPortId is Guid fromId)
            {
                var world = ScreenToWorld(Input.mousePosition);
                if (TryFindCompatiblePort(world, fromId, out var target, out _))
                {
                    try
                    {
                        Project.History.Execute(new ConnectPortsCommand(Project, fromId, target.Id));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Unable to connect: {ex.Message}");
                    }
                }
                CancelWireDrag();
                RebuildAll();
            }

            if (_draggingPanelId is Guid dragId && _dragMoved)
            {
                var panel = Project.Graph.GetPanel(dragId);
                var toX = panel.PositionXMm;
                var toY = panel.PositionYMm;
                panel.SetPosition(_dragOriginMm.x, _dragOriginMm.y);
                Project.History.Execute(new MovePanelCommand(Project, dragId, _dragOriginMm.x, _dragOriginMm.y, toX, toY));
            }

            _draggingPanelId = null;
            _dragMoved = false;
        }

        private void CancelWireDrag()
        {
            _wireFromPortId = null;
            if (_previewWire != null)
            {
                Destroy(_previewWire.gameObject);
                _previewWire = null;
            }
        }

        private void EnsurePreviewWire()
        {
            if (_previewWire != null) return;
            var go = new GameObject("PreviewWire");
            go.transform.SetParent(transform, false);
            _previewWire = go.AddComponent<LineRenderer>();
            _previewWire.positionCount = 2;
            _previewWire.material = new Material(Shader.Find("Sprites/Default"));
            _previewWire.sortingOrder = 20;
            _previewWire.numCapVertices = 4;
        }

        public void RebuildAll()
        {
            RebuildPanels();
            RebuildWiresOnly();
        }

        private void RebuildPanels()
        {
            var alive = new HashSet<Guid>();
            foreach (var panel in Project.Graph.Panels.Values)
            {
                alive.Add(panel.Id);
                if (!_panelViews.TryGetValue(panel.Id, out var view))
                {
                    view = SolarPanelView.Create(_panelsRoot);
                    _panelViews[panel.Id] = view;
                }
                var def = Project.RequireDefinition(panel.DefinitionId);
                view.Bind(panel, def);
                view.SetSelected(_selectedPanelId == panel.Id);
            }

            foreach (var id in _panelViews.Keys.ToList())
            {
                if (alive.Contains(id)) continue;
                Destroy(_panelViews[id].gameObject);
                _panelViews.Remove(id);
            }

            // Keep the first modules framed so they aren't off-camera.
            if (_panelViews.Count > 0 && designCamera != null)
            {
                var first = _panelViews.Values.First();
                var p = first.transform.position;
                designCamera.transform.position = new Vector3(p.x, p.y, designCamera.transform.position.z);
                if (designCamera.orthographicSize < 3f)
                    designCamera.orthographicSize = 4f;
            }
        }

        private void RebuildWiresOnly()
        {
            var alive = new HashSet<Guid>();
            foreach (var conn in Project.Graph.Connections.Values)
            {
                if (!Project.Graph.TryGetPort(conn.StartPortId, out var start)
                    || !Project.Graph.TryGetPort(conn.EndPortId, out var end))
                    continue;

                alive.Add(conn.Id);
                if (!_wireViews.TryGetValue(conn.Id, out var wire))
                {
                    wire = WireView.Create(_wiresRoot, conn.Id);
                    _wireViews[conn.Id] = wire;
                }

                var fromColor = start.Polarity == Polarity.Positive
                    ? new Color(0.83f, 0.18f, 0.18f)
                    : new Color(0.13f, 0.13f, 0.13f);
                var toColor = end.Polarity == Polarity.Positive
                    ? new Color(0.83f, 0.18f, 0.18f)
                    : new Color(0.13f, 0.13f, 0.13f);
                wire.SetEndpoints(GetPortWorld(start), GetPortWorld(end), fromColor, toColor, false);
            }

            foreach (var id in _wireViews.Keys.ToList())
            {
                if (alive.Contains(id)) continue;
                Destroy(_wireViews[id].gameObject);
                _wireViews.Remove(id);
            }
        }

        private void UpdatePortVisibility()
        {
            var showAll = _wireFromPortId is not null;
            foreach (var (id, view) in _panelViews)
                view.SetPortsVisible(showAll || _selectedPanelId == id);
        }

        private Vector3 GetPortWorld(ElectricalPort port)
        {
            if (!_panelViews.TryGetValue(port.OwnerComponentId, out var view))
                return Vector3.zero;
            return view.GetPortWorldPosition(port.Polarity == Polarity.Positive);
        }

        private bool TryHitPort(Vector3 world, out Guid portId, out ElectricalPort port)
        {
            portId = Guid.Empty;
            port = null!;
            var best = PortHitRadiusPx;
            foreach (var panel in Project.Graph.Panels.Values)
            {
                foreach (var p in panel.Ports)
                {
                    if (p.IsOccupied) continue;
                    var wp = GetPortWorld(p);
                    var screenPort = designCamera!.WorldToScreenPoint(wp);
                    var dist = Vector2.Distance(screenPort, Input.mousePosition);
                    if (dist < best)
                    {
                        best = dist;
                        portId = p.Id;
                        port = p;
                    }
                }
            }
            return portId != Guid.Empty;
        }

        private bool TryFindCompatiblePort(Vector3 world, Guid fromPortId, out ElectricalPort target, out Vector3 snapPos)
        {
            target = null!;
            snapPos = world;
            var from = Project.Graph.GetPort(fromPortId);
            if (!Project.Graph.TryGetPanel(from.OwnerComponentId, out var fromOwner))
                return false;

            var best = 0.35f; // meters
            ElectricalPort? bestPort = null;
            foreach (var panel in Project.Graph.Panels.Values)
            {
                if (panel.Id == fromOwner.Id) continue;
                foreach (var p in panel.Ports)
                {
                    if (p.IsOccupied) continue;
                    var validation = ConnectionValidator.ValidateDcConnection(from, p, fromOwner, panel);
                    if (!validation.IsValid) continue;
                    var wp = GetPortWorld(p);
                    var dist = Vector3.Distance(world, wp);
                    if (dist < best)
                    {
                        best = dist;
                        bestPort = p;
                        snapPos = wp;
                    }
                }
            }

            if (bestPort is null) return false;
            target = bestPort;
            return true;
        }

        private (float x, float y) ApplySnap(Guid movingId, float xMm, float yMm, float widthMm, float heightMm)
        {
            float bestX = xMm, bestY = yMm;
            var bestScore = SnapThresholdMm;

            foreach (var other in Project.Graph.Panels.Values)
            {
                if (other.Id == movingId) continue;
                var oDef = Project.RequireDefinition(other.DefinitionId);
                var oSize = WorldScale.PanelSizeMeters(oDef, other.RotationDegrees);
                var ow = oSize.x * WorldScale.MmPerMeter;
                var oh = oSize.y * WorldScale.MmPerMeter;
                var ox = (float)other.PositionXMm;
                var oy = (float)other.PositionYMm;

                Try(ox + ow + PanelSpacingMm, yMm);
                Try(ox - widthMm - PanelSpacingMm, yMm);
                Try(xMm, oy + oh + PanelSpacingMm);
                Try(xMm, oy - heightMm - PanelSpacingMm);
                Try(ox, yMm);
                Try(ox + ow - widthMm, yMm);
                Try(xMm, oy);
                Try(xMm, oy + oh - heightMm);

                void Try(float nx, float ny)
                {
                    var score = Mathf.Abs(nx - xMm) + Mathf.Abs(ny - yMm);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestX = nx;
                        bestY = ny;
                    }
                }
            }

            return (bestX, bestY);
        }

        private Vector3 ScreenToWorld(Vector3 screen)
        {
            screen.z = Mathf.Abs(designCamera!.transform.position.z);
            var w = designCamera.ScreenToWorldPoint(screen);
            w.z = 0f;
            return w;
        }
    }
}
