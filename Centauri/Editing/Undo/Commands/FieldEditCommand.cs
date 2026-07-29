namespace Centauri.Editing.Undo;

// One committed inspector field edit (Widgets' DragRow/SliderRow/ColorRow/CheckRow/Vec2Row, when
// given a CommandHistory) — a drag-to-release gesture on a single field, a slider hop, a checkbox
// toggle, or a right-click "Reset." Generic over the field's own type (float/bool/Vector2/Vector4)
// rather than one command class per field, since every one of these is the same shape: apply a
// captured value through the same setter the live edit itself already used.
internal sealed class FieldEditCommand<T> : ICommand
{
    private readonly Action<T> _apply;
    private readonly T _before;
    private readonly T _after;

    public FieldEditCommand(Action<T> apply, T before, T after)
    {
        _apply  = apply;
        _before = before;
        _after  = after;
    }

    public void Undo() => _apply(_before);
    public void Redo() => _apply(_after);
}
