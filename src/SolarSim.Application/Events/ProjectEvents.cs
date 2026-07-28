namespace SolarSim.Application.Events;

public delegate void SolarSimEventHandler();
public delegate void SolarSimEventHandler<T>(T args);

public sealed class ProjectChangedEventArgs
{
    public string Reason { get; init; } = string.Empty;
}

public sealed class SelectionChangedEventArgs
{
    public IReadOnlyList<Guid> SelectedComponentIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> SelectedConnectionIds { get; init; } = Array.Empty<Guid>();
}
