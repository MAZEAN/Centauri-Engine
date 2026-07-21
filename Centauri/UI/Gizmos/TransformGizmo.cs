namespace Centauri.UI.Gizmos;

using System.Numerics;
using ImGuiNET;

using World;
using Common;

// Screen-space translate gizmo for the selected entity. Drawn with ImGui's *foreground* draw
// list (a 2D overlay on top of everything, no GL render-graph involvement) and driven entirely
// off ImGui's own IO mouse state during the frame — so it needs no new pass, no native
// dependency (ImGuizmo et al.), and nothing from InputSystem beyond a "don't pick while I'm
// busy" handshake via IsInteracting.
//
// The math is deliberately self-contained and mirrors Camera.ScreenPointToRay's conventions
// (row-vector view*proj, the same NDC→screen flip), using the *raw* projection so the handles
// don't inherit the TAA jitter the scene render does. Translate only for now; rotate/scale can
// hang off the same project/hit-test/drag scaffold later.
internal sealed class TransformGizmo
{
    private enum Axis { None, X, Y, Z }

    // Apparent handle length as a fraction of distance-to-camera — keeps the gizmo a roughly
    // constant on-screen size regardless of how far the selection is (a fixed world length would
    // shrink to nothing when zoomed out and swamp the screen up close).
    private const float HandleScreenFraction = 0.14f;

    private const float PickPixels     = 7f;   // cursor-to-axis distance that counts as a hover
    private const float LineThickness  = 3f;
    private const float ArrowPixels    = 13f;
    private const float CentreDotPixels = 3.5f;

    private Axis _hover = Axis.None;
    private Axis _drag  = Axis.None;

    // Reference geometry frozen at drag start, so the world-per-pixel mapping doesn't drift as
    // the object (and thus the projected handle) moves during the drag.
    private Vector2 _dragStartMouse;
    private Vector3 _dragStartWorld;
    private Vector3 _dragAxisDir;
    private Vector2 _dragScreenDir;
    private float   _dragWorldPerPixel;

    // True while the cursor is over a handle or a drag is in progress — InputSystem folds this
    // into WantsMouse so a click on the gizmo doesn't also re-pick/deselect underneath it.
    public bool IsInteracting => _drag != Axis.None || _hover != Axis.None;

    public void Draw(Scene scene, Camera camera)
    {
        if (scene.Selected is not { } entity)
        {
            _hover = Axis.None;
            _drag  = Axis.None;
            return;
        }

        var t        = entity.Transform;
        var origin   = t.WorldPosition;
        var io       = ImGui.GetIO();
        var viewport = io.DisplaySize;
        var viewProj = camera.GetViewMatrix() * camera.GetProjectionMatrixRaw();

        if (!Project(origin, viewProj, viewport, out var oScreen))
        {
            _hover = Axis.None; // selection is behind the camera — nothing to draw or hit-test
            return;
        }

        var dist     = Vector3.Distance(camera.Position, origin);
        var worldLen = MathF.Max(dist * HandleScreenFraction, 1e-3f);

        Span<Vector3> axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        Span<Vector2> ends    = stackalloc Vector2[3];
        Span<bool>    visible = stackalloc bool[3];
        for (var i = 0; i < 3; i++)
            visible[i] = Project(origin + axes[i] * worldLen, viewProj, viewport, out ends[i]);

        UpdateInteraction(t, io, oScreen, ends, visible, axes, origin, worldLen);
        DrawHandles(oScreen, ends, visible);
    }

    private void UpdateInteraction(
        Transform t, ImGuiIOPtr io, Vector2 oScreen,
        ReadOnlySpan<Vector2> ends, ReadOnlySpan<bool> visible, ReadOnlySpan<Vector3> axes,
        Vector3 origin, float worldLen)
    {
        var mouse = io.MousePos;

        if (_drag == Axis.None)
        {
            _hover = Axis.None;

            // Don't hijack the cursor when an ImGui panel (Outliner/Properties) wants it —
            // otherwise a handle behind a panel would steal that panel's clicks.
            if (!io.WantCaptureMouse)
            {
                var best = Widgets.Scale(PickPixels);
                for (var i = 0; i < 3; i++)
                {
                    if (!visible[i]) continue;
                    var d = DistanceToSegment(mouse, oScreen, ends[i]);
                    if (d < best) { best = d; _hover = (Axis)(i + 1); }
                }
            }

            if (_hover != Axis.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                BeginDrag(t, mouse, oScreen, ends, axes, origin, worldLen);
        }

        if (_drag == Axis.None) return;

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            ApplyDrag(t, mouse);
        else
            _drag = Axis.None;
    }

    private void BeginDrag(
        Transform t, Vector2 mouse, Vector2 oScreen,
        ReadOnlySpan<Vector2> ends, ReadOnlySpan<Vector3> axes, Vector3 origin, float worldLen)
    {
        var i          = (int)_hover - 1;
        var screenAxis = ends[i] - oScreen;
        var screenLen  = screenAxis.Length();
        if (screenLen < 1e-3f) return; // handle points straight at the camera — no usable drag axis

        _drag              = _hover;
        _dragStartMouse    = mouse;
        _dragStartWorld    = origin;
        _dragAxisDir       = axes[i];
        _dragScreenDir     = screenAxis / screenLen;
        _dragWorldPerPixel = worldLen / screenLen;
    }

    private void ApplyDrag(Transform t, Vector2 mouse)
    {
        var alongPixels = Vector2.Dot(mouse - _dragStartMouse, _dragScreenDir);
        var newWorld    = _dragStartWorld + _dragAxisDir * (alongPixels * _dragWorldPerPixel);

        // Transform.Position is parent-local; WorldPosition = Transform(local, parentWorld), so
        // invert the parent to turn the desired world position back into a local one. No parent
        // (or a degenerate/non-invertible parent) collapses to newWorld unchanged.
        if (t.Parent is { } parent && Matrix4x4.Invert(parent.WorldMatrix, out var invParent))
            t.Position = Vector3.Transform(newWorld, invParent);
        else
            t.Position = newWorld;
    }

    private void DrawHandles(Vector2 oScreen, ReadOnlySpan<Vector2> ends, ReadOnlySpan<bool> visible)
    {
        var dl = ImGui.GetForegroundDrawList();

        Span<Vector4> baseColors =
        [
            new(0.90f, 0.25f, 0.25f, 1f), // X
            new(0.40f, 0.85f, 0.40f, 1f), // Y
            new(0.35f, 0.55f, 0.95f, 1f), // Z
        ];
        var highlight = new Vector4(1f, 0.85f, 0.20f, 1f);

        for (var i = 0; i < 3; i++)
        {
            if (!visible[i]) continue;

            var axis   = (Axis)(i + 1);
            var active = _drag == axis || (_drag == Axis.None && _hover == axis);
            DrawArrow(dl, oScreen, ends[i], ImGui.GetColorU32(active ? highlight : baseColors[i]));
        }

        dl.AddCircleFilled(oScreen, Widgets.Scale(CentreDotPixels),
            ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f)));
    }

    private static void DrawArrow(ImDrawListPtr dl, Vector2 from, Vector2 to, uint color)
    {
        var dir = to - from;
        var len = dir.Length();
        if (len < 1e-3f) return;
        dir /= len;

        var arrow = Widgets.Scale(ArrowPixels);
        var perp  = new Vector2(-dir.Y, dir.X);
        var baseP = to - dir * arrow;

        dl.AddLine(from, baseP, color, Widgets.Scale(LineThickness));
        dl.AddTriangleFilled(to, baseP + perp * (arrow * 0.4f), baseP - perp * (arrow * 0.4f), color);
    }

    // Row-vector projection matching Camera.ScreenPointToRay: clip = point * (view*proj), then the
    // usual perspective divide and NDC→screen flip. Returns false when the point is at/behind the
    // camera plane (w <= 0), where the divide is meaningless.
    internal static bool Project(Vector3 world, Matrix4x4 viewProj, Vector2 viewport, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-5f)
        {
            screen = default;
            return false;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector2(
            (ndc.X * 0.5f + 0.5f) * viewport.X,
            (1f - (ndc.Y * 0.5f + 0.5f)) * viewport.Y);
        return true;
    }

    internal static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        if (lenSq < 1e-6f) return Vector2.Distance(p, a);

        var tRaw = Vector2.Dot(p - a, ab) / lenSq;
        var t    = Math.Clamp(tRaw, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
