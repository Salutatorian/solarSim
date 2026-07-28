namespace SolarSim.Application.Commands;

public interface ICommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public sealed class CommandHistory
{
    private readonly List<ICommand> _undoStack = new();
    private readonly List<ICommand> _redoStack = new();
    private readonly int _maxEntries;

    public CommandHistory(int maxEntries = 200)
    {
        _maxEntries = Math.Max(10, maxEntries);
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public IReadOnlyList<string> Descriptions => _undoStack.Select(c => c.Description).ToList();

    public event Action? HistoryChanged;

    public void Execute(ICommand command)
    {
        command.Execute();
        _undoStack.Add(command);
        if (_undoStack.Count > _maxEntries)
            _undoStack.RemoveAt(0);
        _redoStack.Clear();
        HistoryChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        command.Undo();
        _redoStack.Add(command);
        HistoryChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        command.Execute();
        _undoStack.Add(command);
        HistoryChanged?.Invoke();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        HistoryChanged?.Invoke();
    }
}
