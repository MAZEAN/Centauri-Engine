namespace Centauri.UI.Panels.Inspector.Sections;

using System.Numerics;

using World;
using Common;

internal sealed class EntityTransformSection
{
    private Vector3 _euler;            // cached working rotation (deg) for the selected entity
    private bool    _editingRotation;  // true while a rotation axis is being dragged

    public void Draw(Entity e)
    {
        using var s = Widgets.Section("Transform");
        if (!s.Open) return;

        var t = e.Transform;
        var a = e.Authored;

        var posReset   = a?.Position ?? Vector3.Zero;
        var rotReset   = a?.Euler    ?? Vector3.Zero;
        var scaleReset = a?.Scale    ?? Vector3.One;

        Widgets.Vec3Rows("Location", t.Position, v => t.Position = v,
            0.05f, "%.3f m", posReset);

        if (!_editingRotation) _euler = t.EulerAngles;

        if (Widgets.Vec3Rows("Rotation", ref _euler, 0.5f, "%.1f°", rotReset, out _editingRotation))
            t.SetEulerAngles(_euler.X, _euler.Y, _euler.Z);

        Widgets.Vec3Rows("Scale", t.Scale, v => t.Scale = v,
            0.01f, "%.3f", scaleReset);

        // A per-axis Scale row alone means resizing something uniformly needs the same number
        // typed/dragged three times. Shows X as the reference value (meaningless once the scale
        // is already non-uniform, same as any single-value display of a 3-component state), but
        // dragging it always sets all three axes together.
        Widgets.DragRow("Uniform Scale", t.Scale.X, v => t.Scale = new Vector3(v, v, v),
            0.01f, 0.001f, 1000f, "%.3f", scaleReset.X);
    }
}
