namespace Centauri.Editing.Undo;

// Bounded undo/redo stack for editor edits (Ctrl+Z / Ctrl+Y — InputSystem.OnKeyDown). Coarse,
// gesture-level granularity: one ICommand per *completed* edit (a finished gizmo drag, an entity
// create/delete), not a per-frame or per-keystroke diff — see each ICommand implementation for
// exactly what "one gesture" means for it. Pure C#, no ImGui/GL, so it's unit-tested directly
// (Centauri.Tests/Editing/CommandHistoryTests.cs) against a fake ICommand rather than needing a
// live scene.
public sealed class CommandHistory
{
    private const int Capacity = 200;

    private readonly LinkedList<ICommand> _undo = new();
    private readonly Stack<ICommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // Records an already-applied command. Never calls Undo/Redo itself — the caller applied the
    // edit before constructing the command (or, for something like a gizmo drag, the edit was
    // already live on screen throughout the gesture), so re-running it here would be redundant at
    // best and wrong for a command whose Redo isn't idempotent.
    //
    // internal, not public: ICommand itself is internal (every implementation lives in this
    // assembly), and CommandHistory only needs to be public at all because it's a parameter type
    // on InputSystem/RenderingSystem/UISystem's public constructors — nothing outside the
    // assembly is meant to construct commands.
    internal void Push(ICommand command)
    {
        _undo.AddLast(command);
        while (_undo.Count > Capacity)
            _undo.RemoveFirst();

        _redo.Clear(); // a fresh edit invalidates whatever redo history existed
    }

    public void Undo()
    {
        if (_undo.Last is not { } node) return;

        _undo.RemoveLast();
        node.Value.Undo();
        _redo.Push(node.Value);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var command)) return;

        command.Redo();
        _undo.AddLast(command);
    }

    // Called on EntitySetLoader.Reset() (Ctrl+Shift+R) — every command on the stack potentially
    // holds a direct reference to an Entity/Transform that Reset is about to dispose and replace
    // with freshly-reloaded ones, so the whole history is invalidated at once rather than left to
    // dangle (see Docs/Documentation/Undo.md's note on object-identity across a delete/recreate).
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
