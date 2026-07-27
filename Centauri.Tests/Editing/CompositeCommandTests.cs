namespace Centauri.Tests.Editing;

using Centauri.Editing.Undo;

// CompositeCommand + CommandHistory.PushRange — the multi-select bulk-edit path (drag/delete every
// selected entity as one Ctrl+Z). Pure C#, spy ICommand, no live scene.
public sealed class CompositeCommandTests
{
    private sealed class OrderSpy : ICommand
    {
        private readonly List<string> _log;
        private readonly string _name;

        public OrderSpy(List<string> log, string name)
        {
            _log  = log;
            _name = name;
        }

        public void Undo() => _log.Add($"undo {_name}");
        public void Redo() => _log.Add($"redo {_name}");
    }

    [Fact]
    public void Undo_RunsEveryCommandsUndo_InReverseOrder()
    {
        var log = new List<string>();
        var composite = new CompositeCommand([new OrderSpy(log, "A"), new OrderSpy(log, "B"), new OrderSpy(log, "C")]);

        composite.Undo();

        Assert.Equal(["undo C", "undo B", "undo A"], log);
    }

    [Fact]
    public void Redo_RunsEveryCommandsRedo_InOriginalOrder()
    {
        var log = new List<string>();
        var composite = new CompositeCommand([new OrderSpy(log, "A"), new OrderSpy(log, "B"), new OrderSpy(log, "C")]);

        composite.Redo();

        Assert.Equal(["redo A", "redo B", "redo C"], log);
    }

    [Fact]
    public void CommandHistory_PushRange_WithZeroCommands_PushesNothing()
    {
        var history = new CommandHistory();

        history.PushRange([]);

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void CommandHistory_PushRange_WithOneCommand_PushesItDirectly_NotWrapped()
    {
        // "Not wrapped" matters observably: a single-entity edit should still undo in exactly one
        // Undo() call, same as it always has — not a behavior change just because the call site
        // now goes through PushRange instead of Push.
        var history = new CommandHistory();
        var log = new List<string>();
        var command = new OrderSpy(log, "solo");

        history.PushRange([command]);
        history.Undo();

        Assert.Equal(["undo solo"], log);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void CommandHistory_PushRange_WithMultipleCommands_UndoesAllOfThemInOneUndoCall()
    {
        var history = new CommandHistory();
        var log = new List<string>();

        history.PushRange([new OrderSpy(log, "A"), new OrderSpy(log, "B")]);
        Assert.True(history.CanUndo);

        history.Undo();

        Assert.Equal(["undo B", "undo A"], log);
        Assert.False(history.CanUndo); // the whole group was one undo step, not two
    }
}
