namespace Centauri.Editing.Undo;

using System.Numerics;
using World;

// Position/Rotation/Scale at some point in time — TransformCommand's before/after pair. A plain
// data record rather than reading live off a Transform each time, so a command's "before" stays
// exactly what it was at drag-start even as the Transform itself keeps changing underneath it.
internal readonly record struct TransformState(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static TransformState Of(Transform t) => new(t.Position, t.Rotation, t.Scale);
}

// One completed gizmo drag (translate/rotate/scale — TransformGizmo.EndDrag), captured at
// drag-end rather than per-frame, so a single drag from grab to release is one undo step
// regardless of how many mouse-move frames it spanned. Restores all three of Position/Rotation/
// Scale on both Undo and Redo rather than just whichever one the active mode actually changed —
// simpler than threading "which field changed" through, and harmless: the other two are just set
// back to the same value they already had.
internal sealed class TransformCommand : ICommand
{
    private readonly Transform _transform;
    private readonly TransformState _before;
    private readonly TransformState _after;

    public TransformCommand(Transform transform, TransformState before, TransformState after)
    {
        _transform = transform;
        _before    = before;
        _after     = after;
    }

    public void Undo() => Apply(_before);
    public void Redo() => Apply(_after);

    private void Apply(TransformState s)
    {
        _transform.Position = s.Position;
        _transform.SetRotation(s.Rotation); // also refreshes the EulerAngles cache the inspector reads
        _transform.Scale    = s.Scale;
    }
}
