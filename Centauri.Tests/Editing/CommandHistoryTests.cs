namespace Centauri.Tests.Editing;

using Centauri.Editing.Undo;

// CommandHistory is pure C# (no ImGui/GL, no live scene) — reachable via internal +
// InternalsVisibleTo, tested here against a small spy ICommand rather than a real gesture.
public sealed class CommandHistoryTests
{
    // Records call order/count across a whole test rather than just "was it called" — several
    // tests below care about *how many times* and *in what order* Undo/Redo fired.
    private sealed class SpyCommand : ICommand
    {
        public int UndoCount { get; private set; }
        public int RedoCount { get; private set; }

        public void Undo() => UndoCount++;
        public void Redo() => RedoCount++;
    }

    [Fact]
    public void FreshHistory_CanUndoAndCanRedo_AreBothFalse()
    {
        var history = new CommandHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_CallsTheCommandsUndo_NotRedo()
    {
        var history = new CommandHistory();
        var command = new SpyCommand();
        history.Push(command);

        history.Undo();

        Assert.Equal(1, command.UndoCount);
        Assert.Equal(0, command.RedoCount);
    }

    [Fact]
    public void Redo_AfterUndo_CallsTheCommandsRedo()
    {
        var history = new CommandHistory();
        var command = new SpyCommand();
        history.Push(command);

        history.Undo();
        history.Redo();

        Assert.Equal(1, command.UndoCount);
        Assert.Equal(1, command.RedoCount);
    }

    [Fact]
    public void Push_NeverCallsUndoOrRedo()
    {
        // The action is assumed already applied by the caller before Push — Push only records.
        var history = new CommandHistory();
        var command = new SpyCommand();

        history.Push(command);

        Assert.Equal(0, command.UndoCount);
        Assert.Equal(0, command.RedoCount);
    }

    [Fact]
    public void MultipleCommands_UndoInReverseOrder()
    {
        var history = new CommandHistory();
        var first  = new SpyCommand();
        var second = new SpyCommand();
        history.Push(first);
        history.Push(second);

        history.Undo(); // should undo `second` (the most recent), not `first`

        Assert.Equal(0, first.UndoCount);
        Assert.Equal(1, second.UndoCount);
    }

    [Fact]
    public void Undo_PastTheBottomOfTheStack_IsANoOp()
    {
        var history = new CommandHistory();
        var command = new SpyCommand();
        history.Push(command);
        history.Undo();

        history.Undo(); // nothing left to undo

        Assert.Equal(1, command.UndoCount); // still just the one real undo
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Redo_WithNothingUndone_IsANoOp()
    {
        var history = new CommandHistory();
        var command = new SpyCommand();
        history.Push(command);

        history.Redo(); // nothing on the redo stack yet

        Assert.Equal(0, command.RedoCount);
    }

    [Fact]
    public void Push_AfterAnUndo_ClearsTheRedoStack()
    {
        // A fresh edit made after undoing should invalidate whatever redo history existed —
        // otherwise Redo could resurrect a command whose target state no longer makes sense next
        // to the new edit.
        var history = new CommandHistory();
        var undone  = new SpyCommand();
        history.Push(undone);
        history.Undo();
        Assert.True(history.CanRedo);

        history.Push(new SpyCommand());

        Assert.False(history.CanRedo);
        history.Redo(); // no-op — confirms the stale command really is gone, not just hidden
        Assert.Equal(0, undone.RedoCount);
    }

    [Fact]
    public void CanUndoCanRedo_TrackStateThroughAFullCycle()
    {
        var history = new CommandHistory();
        var command = new SpyCommand();

        history.Push(command);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        history.Undo();
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Clear_DropsBothStacks()
    {
        var history = new CommandHistory();
        var undoneCommand = new SpyCommand();
        history.Push(undoneCommand);
        history.Push(new SpyCommand());
        history.Undo(); // one on the undo stack, one on the redo stack now

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        history.Redo(); // no-op — confirms Clear actually dropped the redo stack, not just undo
        Assert.Equal(0, undoneCommand.RedoCount);
    }

    [Fact]
    public void Push_BeyondCapacity_DropsTheOldestCommand()
    {
        var history = new CommandHistory();
        var oldest = new SpyCommand();
        history.Push(oldest);

        for (var i = 0; i < 200; i++)
            history.Push(new SpyCommand()); // pushes the stack past its 200-command capacity

        // Undo everything left on the stack (should be exactly 200 — the cap — none of them the
        // original `oldest`, which should have been evicted to make room).
        var undoCount = 0;
        while (history.CanUndo)
        {
            history.Undo();
            undoCount++;
        }

        Assert.Equal(200, undoCount);
        Assert.Equal(0, oldest.UndoCount); // evicted, never reached by Undo
    }
}
