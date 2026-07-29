namespace Centauri.Editing.Undo;

// Wraps several already-applied commands as one undo step — a multi-select bulk edit (drag every
// selected entity together, delete every selected entity) should be one Ctrl+Z, not one per
// entity. Undo runs in reverse order, Redo in the original order; the individual commands here are
// independent of each other (different entities), so order doesn't change the *result*, only
// keeps the more intuitive "unwind newest-first" feel consistent with CommandHistory itself.
internal sealed class CompositeCommand : ICommand
{
    private readonly IReadOnlyList<ICommand> _commands;

    public CompositeCommand(IReadOnlyList<ICommand> commands) => _commands = commands;

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }

    public void Redo()
    {
        foreach (var command in _commands)
            command.Redo();
    }
}
