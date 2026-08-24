using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SolarSim.Application.Commands;
using SolarSim.Application.Project;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Desktop;

public sealed class DesignCanvasControl : Control
{
    private SolarProject? _project;
    private double _cameraXMm;
    private double _cameraYMm;
    private double _zoom = 1;
    private bool _panning;
    private bool _dragging;
    private Point _lastPointer;
    private Guid? _dragPanelId;
    private double _dragStartX;
    private double _dragStartY;
    private double _dragOrigX;
    private double _dragOrigY;

    private static readonly IBrush StageFill = new SolidColorBrush(Color.FromRgb(11, 18, 32));
    private static readonly IBrush RoofFill = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));
    private static readonly IPen RoofPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 1.5);
    private static readonly IBrush PanelFill = new SolidColorBrush(Color.FromRgb(30, 58, 95));
    private static readonly IPen PanelPen = new Pen(new SolidColorBrush(Color.FromRgb(96, 165, 250)), 1);
    private static readonly IPen PanelSelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(251, 191, 36)), 2);
    private static readonly IBrush EquipFill = new SolidColorBrush(Color.FromRgb(31, 41, 55));
    private static readonly IPen EquipPen = new Pen(new SolidColorBrush(Color.FromRgb(156, 163, 175)), 1);
    private static readonly IPen WirePen = new Pen(new SolidColorBrush(Color.FromRgb(234, 179, 8)), 1.5);

    public SolarProject? Project
    {
        get => _project;
        set
        {
            _project = value;
            InvalidateVisual();
        }
    }

    public Guid? SelectedPanelId { get; set; }

    public DesignCanvasControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public (double X, double Y) WorldCenterMm()
    {
        var ppm = PixelsPerMm();
        if (ppm <= 0) return (0, 0);
        return (_cameraXMm + Bounds.Width / 2 / ppm, _cameraYMm + Bounds.Height / 2 / ppm);
    }

    public void FitToContent()
    {
        if (_project is null || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            InvalidateVisual();
            return;
        }

        var (minX, minY, maxX, maxY) = WorldBounds();
        var worldW = Math.Max(maxX - minX, 2000);
        var worldH = Math.Max(maxY - minY, 2000);
        var pad = 1.15;
        var zoomX = Bounds.Width / (worldW * pad * 0.04);
        var zoomY = Bounds.Height / (worldH * pad * 0.04);
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.01, 8);
        var ppm = PixelsPerMm();
        _cameraXMm = minX - (Bounds.Width / ppm - worldW) / 2;
        _cameraYMm = minY - (Bounds.Height / ppm - worldH) / 2;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var factor = e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        var oldPpm = PixelsPerMm();
        var world = ScreenToWorld(e.GetPosition(this));
        _zoom = Math.Clamp(_zoom * factor, 0.01, 12);
        var newPpm = PixelsPerMm();
        var p = e.GetPosition(this);
        _cameraXMm = world.X - p.X / newPpm;
        _cameraYMm = world.Y - p.Y / newPpm;
        _ = oldPpm;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        _lastPointer = p;
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed || props.IsMiddleButtonPressed)
        {
            _panning = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (_project is not null && HitPanel(ScreenToWorld(p)) is Guid id)
        {
            SelectedPanelId = id;
            _project.Selection.SetSelection(componentIds: [id]);
            var panel = _project.Graph.GetPanel(id);
            _dragging = true;
            _dragPanelId = id;
            _dragStartX = p.X;
            _dragStartY = p.Y;
            _dragOrigX = panel.PositionXMm;
            _dragOrigY = panel.PositionYMm;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        SelectedPanelId = null;
        _project?.Selection.Clear();
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var dx = p.X - _lastPointer.X;
        var dy = p.Y - _lastPointer.Y;
        _lastPointer = p;

        if (_panning)
        {
            var ppm = PixelsPerMm();
            _cameraXMm -= dx / ppm;
            _cameraYMm -= dy / ppm;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_dragging && _dragPanelId is Guid id && _project is not null)
        {
            var ppm = PixelsPerMm();
            var panel = _project.Graph.GetPanel(id);
            panel.SetPosition(_dragOrigX + (p.X - _dragStartX) / ppm, _dragOrigY + (p.Y - _dragStartY) / ppm);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging && _dragPanelId is Guid id && _project is not null)
        {
            var panel = _project.Graph.GetPanel(id);
            if (Math.Abs(panel.PositionXMm - _dragOrigX) > 0.5 || Math.Abs(panel.PositionYMm - _dragOrigY) > 0.5)
            {
                _project.History.Execute(new MovePanelCommand(
                    _project, id, _dragOrigX, _dragOrigY, panel.PositionXMm, panel.PositionYMm));
            }
        }

        _panning = false;
        _dragging = false;
        _dragPanelId = null;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(StageFill, new Rect(Bounds.Size));
        if (_project is null) return;

        var ppm = PixelsPerMm();
        DrawGrid(context, ppm);

        foreach (var roof in _project.Roofs.Roofs.Where(r => r.IsVisible && r.Vertices.Count >= 2))
            DrawRoof(context, roof, ppm);

        foreach (var conn in _project.Graph.Connections.Values)
            DrawWire(context, conn, ppm);

        foreach (var eq in _project.Graph.Equipment.Values)
        {
            var rect = new Rect(WorldToScreen(eq.PositionXMm, eq.PositionYMm), new Size(eq.WidthMm * ppm, eq.HeightMm * ppm));
            context.DrawRectangle(EquipFill, EquipPen, rect, 3, 3);
        }

        foreach (var panel in _project.Graph.Panels.Values)
        {
            var (w, h) = _project.GetPanelFootprintMm(panel);
            var rect = new Rect(WorldToScreen(panel.PositionXMm, panel.PositionYMm), new Size(w * ppm, h * ppm));
            var pen = panel.Id == SelectedPanelId ? PanelSelectedPen : PanelPen;
            context.DrawRectangle(PanelFill, pen, rect, 2, 2);
        }
    }

    private void DrawRoof(DrawingContext context, RoofSurface roof, double ppm)
    {
        if (roof.Vertices.Count < 2) return;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var first = WorldToScreen(roof.Vertices[0].X, roof.Vertices[0].Y);
            ctx.BeginFigure(first, isFilled: roof.IsClosed);
            for (var i = 1; i < roof.Vertices.Count; i++)
                ctx.LineTo(WorldToScreen(roof.Vertices[i].X, roof.Vertices[i].Y));
            ctx.EndFigure(roof.IsClosed);
        }
        context.DrawGeometry(roof.IsClosed ? RoofFill : null, RoofPen, geo);
    }

    private void DrawWire(DrawingContext context, ElectricalConnection conn, double ppm)
    {
        if (_project is null) return;
        if (!_project.Graph.TryGetPort(conn.StartPortId, out var a)
            || !_project.Graph.TryGetPort(conn.EndPortId, out var b))
            return;

        var pa = PortWorld(a);
        var pb = PortWorld(b);
        context.DrawLine(WirePen, WorldToScreen(pa.X, pa.Y), WorldToScreen(pb.X, pb.Y));
        _ = ppm;
    }

    private Point2Mm PortWorld(ElectricalPort port)
    {
        if (_project!.Graph.TryGetPanel(port.OwnerComponentId, out var panel))
        {
            var (w, h) = _project.GetPanelFootprintMm(panel);
            var local = PanelPortLayoutService.ForAxisAlignedPanel(w, h);
            if (port.PortType == PortType.PVNegative)
                return new Point2Mm(panel.PositionXMm + local.NegLocalXMm, panel.PositionYMm + local.NegLocalYMm);
            return new Point2Mm(panel.PositionXMm + local.PosLocalXMm, panel.PositionYMm + local.PosLocalYMm);
        }

        if (_project.Graph.Equipment.TryGetValue(port.OwnerComponentId, out var eq))
            return new Point2Mm(eq.PositionXMm + eq.WidthMm / 2, eq.PositionYMm + eq.HeightMm / 2);

        return new Point2Mm(0, 0);
    }

    private void DrawGrid(DrawingContext context, double ppm)
    {
        var stepMm = 1000;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 148, 163, 184)), 1);
        var min = ScreenToWorld(new Point(0, 0));
        var max = ScreenToWorld(new Point(Bounds.Width, Bounds.Height));
        var x0 = Math.Floor(min.X / stepMm) * stepMm;
        var y0 = Math.Floor(min.Y / stepMm) * stepMm;
        for (var x = x0; x <= max.X; x += stepMm)
            context.DrawLine(pen, WorldToScreen(x, min.Y), WorldToScreen(x, max.Y));
        for (var y = y0; y <= max.Y; y += stepMm)
            context.DrawLine(pen, WorldToScreen(min.X, y), WorldToScreen(max.X, y));
        _ = ppm;
    }

    private Guid? HitPanel(Point2Mm world)
    {
        if (_project is null) return null;
        foreach (var panel in _project.Graph.Panels.Values.Reverse())
        {
            var (w, h) = _project.GetPanelFootprintMm(panel);
            if (world.X >= panel.PositionXMm && world.X <= panel.PositionXMm + w
                && world.Y >= panel.PositionYMm && world.Y <= panel.PositionYMm + h)
                return panel.Id;
        }
        return null;
    }

    private (double MinX, double MinY, double MaxX, double MaxY) WorldBounds()
    {
        var xs = new List<double> { 0, 12000 };
        var ys = new List<double> { 0, 8000 };
        if (_project is null) return (0, 0, 12000, 8000);

        foreach (var roof in _project.Roofs.Roofs)
        {
            foreach (var v in roof.Vertices)
            {
                xs.Add(v.X);
                ys.Add(v.Y);
            }
        }

        foreach (var panel in _project.Graph.Panels.Values)
        {
            var (w, h) = _project.GetPanelFootprintMm(panel);
            xs.Add(panel.PositionXMm);
            xs.Add(panel.PositionXMm + w);
            ys.Add(panel.PositionYMm);
            ys.Add(panel.PositionYMm + h);
        }

        return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private double PixelsPerMm() => 0.04 * _zoom;

    private Point WorldToScreen(double xMm, double yMm)
    {
        var ppm = PixelsPerMm();
        return new Point((xMm - _cameraXMm) * ppm, (yMm - _cameraYMm) * ppm);
    }

    private Point2Mm ScreenToWorld(Point p)
    {
        var ppm = PixelsPerMm();
        return new Point2Mm(_cameraXMm + p.X / ppm, _cameraYMm + p.Y / ppm);
    }
}
