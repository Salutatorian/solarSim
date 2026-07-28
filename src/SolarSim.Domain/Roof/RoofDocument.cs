namespace SolarSim.Domain.Roof;

/// <summary>
/// Multi-roof document: L-shapes and complex plans are multiple roof layers.
/// Panel placement is valid if the panel fits inside at least one visible closed roof.
/// </summary>
public sealed class RoofDocument
{
    private readonly List<RoofSurface> _roofs = new();

    public IReadOnlyList<RoofSurface> Roofs => _roofs;
    public Guid? ActiveRoofId { get; private set; }

    public RoofSurface? ActiveRoof =>
        ActiveRoofId is Guid id
            ? _roofs.FirstOrDefault(r => r.Id == id)
            : _roofs.FirstOrDefault();

    public bool HasAnyClosedRoof => _roofs.Any(r => r.HasRoof && r.IsVisible);

    public RoofSurface EnsureActiveRoof()
    {
        if (ActiveRoof is { } existing)
            return existing;

        var roof = new RoofSurface(Guid.NewGuid(), $"Roof {_roofs.Count + 1}");
        _roofs.Add(roof);
        ActiveRoofId = roof.Id;
        return roof;
    }

    public RoofSurface AddRoof(string? name = null)
    {
        var roof = new RoofSurface(Guid.NewGuid(), name ?? $"Roof {_roofs.Count + 1}");
        _roofs.Add(roof);
        ActiveRoofId = roof.Id;
        return roof;
    }

    public void AddExisting(RoofSurface roof, bool makeActive = true)
    {
        if (_roofs.Any(r => r.Id == roof.Id))
            throw new InvalidOperationException($"Roof {roof.Id} already exists.");
        _roofs.Add(roof);
        if (makeActive || ActiveRoofId is null)
            ActiveRoofId = roof.Id;
    }

    public bool RemoveRoof(Guid id)
    {
        var removed = _roofs.RemoveAll(r => r.Id == id) > 0;
        if (!removed) return false;
        if (ActiveRoofId == id)
            ActiveRoofId = _roofs.FirstOrDefault()?.Id;
        return true;
    }

    public bool SetActive(Guid id)
    {
        if (_roofs.All(r => r.Id != id)) return false;
        ActiveRoofId = id;
        return true;
    }

    public RoofSurface? Find(Guid id) => _roofs.FirstOrDefault(r => r.Id == id);

    public void Clear()
    {
        _roofs.Clear();
        ActiveRoofId = null;
    }

    public double TotalAreaSquareMeters() =>
        _roofs.Where(r => r.HasRoof && r.IsVisible).Sum(r => r.AreaSquareMeters());
}
