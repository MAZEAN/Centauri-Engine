namespace Centauri.UI.Gizmos;

using System.Numerics;
using ImGuiNET;

using World;
using Common;

// Screen-space transform gizmo for the selected entity — translate / rotate / scale, switched with
// W / E / R. This class owns the *interaction*: mode, hover/drag state, and turning mouse motion
// into Transform edits. The pure geometry lives in GizmoMath and the rendering in GizmoDraw.
//
// It's driven entirely off ImGui's own IO mouse/keyboard state during the frame and draws into the
// foreground draw list — so it needs no new pass, no native dependency (ImGuizmo et al.), and
// nothing from InputSystem beyond a "don't pick while I'm busy" handshake via IsInteracting.
// Translate and rotate act on world axes; scale acts on the object's *local* basis (Transform.Scale
// is local — a world-axis scale of a rotated object isn't representable by it). See
// Docs/Documentation/Gizmos.md.
internal sealed class TransformGizmo
{
    private enum Axis { None, X, Y, Z }
    private enum Mode { Translate, Rotate, Scale }

    // Apparent handle length as a fraction of distance-to-camera — keeps the gizmo a roughly
    // constant on-screen size regardless of how far the selection is (a fixed world length would
    // shrink to nothing when zoomed out and swamp the screen up close).
    private const float HandleScreenFraction = 0.1f;

    private const float PickPixels       = 10f;  // cursor-to-handle distance that counts as a hover
    private const float ScaleSensitivity = 0.01f; // per-pixel fractional change when dragging scale
    private const float MinScale         = 1e-3f;

    // Rotate feel — see GizmoMath.RotationAngleDelta. Gain 1 keeps the initial sensitivity identical
    // to the exact angle-around-centre map; the radius floor stops a grab near the centre from
    // becoming hypersensitive.
    private const float RotateGain            = 1f;
    private const float MinRotateRadiusPixels = 8f;

    private Mode _mode  = Mode.Translate;
    private Axis _hover = Axis.None;
    private Axis _drag  = Axis.None;

    // Reference state frozen at drag start so the mapping doesn't drift as the object (and thus the
    // projected handle) moves mid-drag. Which fields are live depends on the mode.
    private int        _dragAxisIndex;
    private Vector2    _dragStartMouse;
    private Vector2    _dragScreenDir;       // translate + scale: unit screen direction of the axis
    private Vector3    _dragStartWorld;      // translate
    private Vector3    _dragAxisDir;         // translate: world axis being slid along
    private float      _dragWorldPerPixel;   // translate
    private Vector3    _dragStartScale;      // scale
    private Quaternion _dragStartRot;        // rotate
    private Vector3    _dragRotAxis;         // rotate: world axis being spun about
    private Vector2    _dragRotRadialHat;    // rotate: unit centre→grab screen direction, frozen
    private float      _dragRotInvRadius;    // rotate: 1 / (grab distance from centre), frozen
    private float      _dragRotSign;         // rotate: screen→world handedness for this axis

    // True while the cursor is over a handle or a drag is in progress — InputSystem folds this into
    // WantsMouse so a click on the gizmo doesn't also re-pick/deselect underneath it.
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
        var io        = ImGui.GetIO();
        var viewport = io.DisplaySize;
        var viewProj = camera.GetViewMatrix() * camera.GetProjectionMatrixRaw();

        if (!GizmoMath.Project(origin, viewProj, viewport, out var oScreen))
        {
            _hover = Axis.None; // selection is behind the camera — nothing to draw or hit-test
            return;
        }

        if (_drag == Axis.None)
            HandleModeSwitch(io);

        var worldLen = MathF.Max(Vector3.Distance(camera.Position, origin) * HandleScreenFraction, 1e-3f);

        if (_mode == Mode.Rotate)
            RotateMode(t, io, camera, origin, oScreen, worldLen, viewProj, viewport);
        else
            LinearMode(t, io, origin, oScreen, worldLen, viewProj, viewport, isScale: _mode == Mode.Scale);
    }

    // The one axis drawn highlighted: the drag axis if dragging, else the hovered one, else none.
    private int ActiveAxis() =>
        _drag != Axis.None ? (int)_drag - 1 : _hover != Axis.None ? (int)_hover - 1 : -1;

    // W/E/R select translate/rotate/scale — read straight off ImGui IO, but only when no text field
    // wants the keyboard and no modifier is held (so Ctrl+Shift+R's scene-reset doesn't also trip R).
    private void HandleModeSwitch(ImGuiIOPtr io)
    {
        if (io.WantCaptureKeyboard || io.KeyCtrl || io.KeyShift || io.KeyAlt) return;

        if (ImGui.IsKeyPressed(ImGuiKey.W, repeat: false))
            _mode = Mode.Translate;
        else if (ImGui.IsKeyPressed(ImGuiKey.E, repeat: false))
            _mode = Mode.Rotate;
        else if (ImGui.IsKeyPressed(ImGuiKey.R, repeat: false))
            _mode = Mode.Scale;
    }

    // ---- Translate + Scale: three straight axis handles ---------------------------------------
    // Identical geometry/hit-test; they differ only in the axis frame (world vs. the object's local
    // basis), the tip glyph (arrow vs. box), and what the drag writes (Position vs. Scale).
    private void LinearMode(Transform t, ImGuiIOPtr io, Vector3 origin, Vector2 oScreen,
        float worldLen, Matrix4x4 viewProj, Vector2 viewport, bool isScale)
    {
        Span<Vector3> axes = stackalloc Vector3[3];
        GizmoMath.AxisDirections(t, isScale, axes);

        Span<Vector2> ends    = stackalloc Vector2[3];
        Span<bool>    visible = stackalloc bool[3];
        for (var i = 0; i < 3; i++)
            visible[i] = GizmoMath.Project(origin + axes[i] * worldLen, viewProj, viewport, out ends[i]);

        var mouse = io.MousePos;

        if (_drag == Axis.None)
        {
            _hover = NearestHandle(mouse, oScreen, ends, visible, io.WantCaptureMouse);

            if (_hover != Axis.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                BeginLinearDrag(t, mouse, oScreen, ends, axes, origin, worldLen, isScale);
        }

        if (_drag != Axis.None)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (isScale) ApplyScaleDrag(t, mouse);
                else         ApplyTranslateDrag(t, mouse);
            }
            else
                _drag = Axis.None;
        }

        GizmoDraw.LinearHandles(oScreen, ends, visible, isScale, ActiveAxis());
    }

    private void BeginLinearDrag(Transform t, Vector2 mouse, Vector2 oScreen, ReadOnlySpan<Vector2> ends,
        ReadOnlySpan<Vector3> axes, Vector3 origin, float worldLen, bool isScale)
    {
        var i          = (int)_hover - 1;
        var screenAxis = ends[i] - oScreen;
        var screenLen  = screenAxis.Length();
        if (screenLen < 1e-3f) return; // handle points straight at the camera — no usable drag axis

        _drag           = _hover;
        _dragAxisIndex  = i;
        _dragStartMouse = mouse;
        _dragScreenDir  = screenAxis / screenLen;

        if (isScale)
        {
            _dragStartScale = t.Scale;
        }
        else
        {
            _dragStartWorld    = origin;
            _dragAxisDir       = axes[i];
            _dragWorldPerPixel = worldLen / screenLen;
        }
    }

    private void ApplyTranslateDrag(Transform t, Vector2 mouse)
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

    private void ApplyScaleDrag(Transform t, Vector2 mouse)
    {
        var alongPixels = Vector2.Dot(mouse - _dragStartMouse, _dragScreenDir);
        var factor      = MathF.Max(MinScale, 1f + alongPixels * ScaleSensitivity);
        var scaled      = MathF.Max(MinScale, GizmoMath.GetComponent(_dragStartScale, _dragAxisIndex) * factor);

        t.Scale = GizmoMath.WithComponent(_dragStartScale, _dragAxisIndex, scaled);
    }

    // ---- Rotate: three world-axis rings -------------------------------------------------------
    private void RotateMode(Transform t, ImGuiIOPtr io, Camera camera, Vector3 origin, Vector2 oScreen,
        float worldLen, Matrix4x4 viewProj, Vector2 viewport)
    {
        Span<Vector3> axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        var mouse = io.MousePos;

        if (_drag == Axis.None)
        {
            _hover = Axis.None;
            if (!io.WantCaptureMouse)
            {
                var best = Widgets.Scale(PickPixels);
                for (var i = 0; i < 3; i++)
                {
                    var d = GizmoMath.DistanceToRing(mouse, origin, axes[i], worldLen, viewProj, viewport);
                    if (!(d < best)) continue;

                    best   = d;
                    _hover = (Axis)(i + 1);
                }
            }

            if (_hover != Axis.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                BeginRotateDrag(t, camera, mouse, oScreen, axes);
        }

        if (_drag != Axis.None)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                ApplyRotateDrag(t, mouse);
            else
                _drag = Axis.None;
        }

        GizmoDraw.Rings(origin, axes, worldLen, viewProj, viewport, oScreen, ActiveAxis());
    }

    private void BeginRotateDrag(Transform t, Camera camera, Vector2 mouse, Vector2 oScreen, ReadOnlySpan<Vector3> axes)
    {
        var i      = (int)_hover - 1;
        var radial = mouse - oScreen;
        var radius = MathF.Max(radial.Length(), Widgets.Scale(MinRotateRadiusPixels));

        _drag             = _hover;
        _dragStartRot     = t.Rotation;
        _dragRotAxis      = axes[i];
        _dragStartMouse   = mouse;
        _dragRotRadialHat = radial / radius;
        _dragRotInvRadius = 1f / radius;

        // Screen atan2 grows clockwise (screen Y is down); a positive right-handed turn about the
        // axis looks CCW when the axis points toward the camera and CW when it points away — so the
        // sign that keeps the drag matching the grabbed ring is sign(camForward·axis).
        _dragRotSign = MathF.Sign(Vector3.Dot(camera.Forward, axes[i]));
        if (_dragRotSign == 0f)
            _dragRotSign = 1f;
    }

    private void ApplyRotateDrag(Transform t, Vector2 mouse)
    {
        var phi = GizmoMath.RotationAngleDelta(_dragRotRadialHat, _dragRotInvRadius, mouse - _dragStartMouse, _dragRotSign, RotateGain);
        t.SetRotation(GizmoMath.ComposeWorldRotation(_dragStartRot, _dragRotAxis, phi));
    }

    // Nearest straight handle within the pick radius. Doesn't hijack the cursor when an ImGui panel
    // (Outliner/Properties) wants it — otherwise a handle behind a panel would steal its clicks.
    private static Axis NearestHandle(Vector2 mouse, Vector2 oScreen, ReadOnlySpan<Vector2> ends,
        ReadOnlySpan<bool> visible, bool imGuiWantsMouse)
    {
        if (imGuiWantsMouse) return Axis.None;

        var hover = Axis.None;
        var best  = Widgets.Scale(PickPixels);
        for (var i = 0; i < 3; i++)
        {
            if (!visible[i]) continue;

            var d = GizmoMath.DistanceToSegment(mouse, oScreen, ends[i]);
            if (!(d < best)) continue;

            best  = d;
            hover = (Axis)(i + 1);
        }
        return hover;
    }
}
