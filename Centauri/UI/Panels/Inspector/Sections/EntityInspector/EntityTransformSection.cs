namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;
using System.Numerics;

using World;
using Common;
using Editing.Undo;

// Location/Rotation/Scale/Uniform-Scale drag rows for the selected entity — the inspector-level
// counterpart to TransformGizmo's viewport drag. Reuses the exact same TransformCommand/
// TransformState the gizmo pushes to CommandHistory (see Docs/Documentation/Undo.md), rather than
// a separate per-field command, so a whole gesture (any mix of the four rows dragged together or
// in sequence before the mouse fully leaves all of them) is one Ctrl+Z — matching how the gizmo
// itself treats a drag as one step regardless of how many frames it spanned.
internal sealed class EntityTransformSection
{
    private Vector3 _euler;            // cached working rotation (deg) for the selected entity
    private bool    _editingRotation;  // true while a rotation axis is being dragged

    // Captured the moment any row goes from idle to active (see Draw's speculative snapshot
    // below); committed as one TransformCommand once every row goes back to idle.
    private TransformState? _dragStart;
    private bool _transformActive;

    public void Draw(Entity e, CommandHistory? undo)
    {
        using var s = Widgets.Section("Transform");
        if (!s.Open) return;

        var t = e.Transform;
        var a = e.Authored;

        var posReset   = a?.Position ?? Vector3.Zero;
        var rotReset   = a?.Euler    ?? Vector3.Zero;
        var scaleReset = a?.Scale    ?? Vector3.One;

        // Speculative pre-drag snapshot — taken on every frame the section was idle last frame,
        // so if this frame turns out to start a new drag, it's already holding the correct
        // before-state (ImGui doesn't apply a drag's first value change on its activation frame,
        // so capturing before the rows below run is safe even on the frame a drag begins).
        if (undo != null && !_transformActive)
            _dragStart = TransformState.Of(t);

        var active = false;

        var pos = t.Position;
        if (Widgets.Vec3Rows("Location", ref pos, 0.05f, "%.3f m", posReset, out var posActive))
            t.Position = pos;
        active |= posActive;

        if (!_editingRotation) _euler = t.EulerAngles;

        if (Widgets.Vec3Rows("Rotation", ref _euler, 0.5f, "%.1f°", rotReset, out _editingRotation))
            t.SetEulerAngles(_euler.X, _euler.Y, _euler.Z);
        active |= _editingRotation;

        var scale = t.Scale;
        if (Widgets.Vec3Rows("Scale", ref scale, 0.01f, "%.3f", scaleReset, out var scaleActive))
            t.Scale = scale;
        active |= scaleActive;

        // A per-axis Scale row alone means resizing something uniformly needs the same number
        // typed/dragged three times. Shows X as the reference value (meaningless once the scale
        // is already non-uniform, same as any single-value display of a 3-component state), but
        // dragging it always sets all three axes together.
        Widgets.DragRow("Uniform Scale", t.Scale.X, v => t.Scale = new Vector3(v, v, v),
            0.01f, 0.001f, 1000f, "%.3f", scaleReset.X);
        active |= ImGui.IsItemActive();

        if (undo != null)
        {
            if (!active && _transformActive && _dragStart is { } start)
            {
                var after = TransformState.Of(t);
                if (after != start)
                    undo.Push(new TransformCommand(t, start, after));
            }
            _transformActive = active;
        }
    }
}
