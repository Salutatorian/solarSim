namespace SolarSim.Domain.Roof;

public readonly record struct Point2Mm(double X, double Y)
{
    public double DistanceTo(Point2Mm other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public enum RoofObstacleKind
{
    Vent,
    Chimney,
    Skylight,
    AcUnit,
    Antenna,
    Custom,
}

public sealed class RoofObstacle
{
    public Guid Id { get; }
    public RoofObstacleKind Kind { get; set; }
    public string Label { get; set; }
    public double XMm { get; set; }
    public double YMm { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public bool AllowOverlap { get; set; }

    public RoofObstacle(
        Guid id,
        RoofObstacleKind kind,
        double xMm,
        double yMm,
        double widthMm,
        double heightMm,
        string? label = null,
        bool allowOverlap = false)
    {
        if (widthMm <= 0) throw new ArgumentOutOfRangeException(nameof(widthMm));
        if (heightMm <= 0) throw new ArgumentOutOfRangeException(nameof(heightMm));

        Id = id;
        Kind = kind;
        XMm = xMm;
        YMm = yMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
        Label = label ?? kind.ToString();
        AllowOverlap = allowOverlap;
    }

    public bool IntersectsAxisAlignedRect(double rectX, double rectY, double rectW, double rectH)
    {
        if (AllowOverlap) return false;
        return rectX < XMm + WidthMm
               && rectX + rectW > XMm
               && rectY < YMm + HeightMm
               && rectY + rectH > YMm;
    }
}

/// <summary>
/// Editable roof plan polygon in millimeters. Multiple roofs compose complex shapes (L, T, …).
/// </summary>
public sealed class RoofSurface
{
    private readonly List<Point2Mm> _vertices = new();
    private readonly List<RoofObstacle> _obstacles = new();

    public Guid Id { get; }
    public string Name { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool IsClosed { get; private set; }
    public double SetbackMm { get; set; } = 457.2; // ~18 in default
    public bool EnforceSetback { get; set; } = true;
    public bool EnforceBoundary { get; set; } = true;
    public bool EnforceObstacles { get; set; } = true;

    public IReadOnlyList<Point2Mm> Vertices => _vertices;
    public IReadOnlyList<RoofObstacle> Obstacles => _obstacles;

    public bool HasRoof => IsClosed && _vertices.Count >= 3;

    public RoofSurface(Guid id, string name = "Roof")
    {
        Id = id;
        Name = name;
    }

    public RoofSurface() : this(Guid.NewGuid())
    {
    }

    public void Clear()
    {
        _vertices.Clear();
        _obstacles.Clear();
        IsClosed = false;
    }

    public void SetVertices(IEnumerable<Point2Mm> vertices, bool closed)
    {
        _vertices.Clear();
        _vertices.AddRange(vertices);
        IsClosed = closed && _vertices.Count >= 3;
        if (_vertices.Count < 3)
            IsClosed = false;
    }

    public void AddVertex(Point2Mm point)
    {
        if (IsLocked)
            throw new InvalidOperationException("Roof layer is locked.");
        if (IsClosed)
            throw new InvalidOperationException("Roof is already closed. Unlock/edit to change vertices.");
        _vertices.Add(point);
    }

    public void InsertVertex(int index, Point2Mm point)
    {
        if (index < 0 || index > _vertices.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _vertices.Insert(index, point);
    }

    public void MoveVertex(int index, Point2Mm point)
    {
        if (IsLocked)
            throw new InvalidOperationException("Roof layer is locked.");
        if (index < 0 || index >= _vertices.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _vertices[index] = point;
    }

    public void RemoveVertex(int index)
    {
        if (index < 0 || index >= _vertices.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _vertices.RemoveAt(index);
        if (_vertices.Count < 3)
            IsClosed = false;
    }

    public bool TryClose()
    {
        if (_vertices.Count < 3)
            return false;
        IsClosed = true;
        return true;
    }

    public void OpenForEdit() => IsClosed = false;

    public void AddObstacle(RoofObstacle obstacle) => _obstacles.Add(obstacle);

    public bool RemoveObstacle(Guid id) => _obstacles.RemoveAll(o => o.Id == id) > 0;

    public RoofObstacle? FindObstacle(Guid id) => _obstacles.FirstOrDefault(o => o.Id == id);

    public double AreaSquareMeters() => RoofGeometry.PolygonAreaSquareMm(_vertices) / 1_000_000.0;

    public IReadOnlyList<(Point2Mm A, Point2Mm B, double LengthMm)> EdgeMeasurements()
    {
        if (_vertices.Count < 2)
            return Array.Empty<(Point2Mm, Point2Mm, double)>();

        var edges = new List<(Point2Mm, Point2Mm, double)>();
        var count = IsClosed ? _vertices.Count : _vertices.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var a = _vertices[i];
            var b = _vertices[(i + 1) % _vertices.Count];
            edges.Add((a, b, a.DistanceTo(b)));
        }
        return edges;
    }
}
