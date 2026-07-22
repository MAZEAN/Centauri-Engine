namespace Centauri.UI.Gizmos;

using System.Numerics;
using ImGuiNET;

using World;
using Common;

// Screen-space transform gizmo for the selected entity — translate / rotate / scale, switched with
// W / E / R. Drawn with ImGui's *foreground* draw list (a 2D overlay on top of everything, no GL
// render-graph involvement) and driven entirely off ImGui's own IO mouse/keyboard state during the
// frame — so it needs no new pass, no native dependency (ImGuizmo et al.), and nothing from
// InputSystem beyond a "don't pick while I'm busy" handshake via IsInteracting.
//
// All three modes share one project → hit-test → drag scaffold. Projection mirrors
// Camera.ScreenPointToRay's conventions (row-vector view*proj, the same NDC→screen flip) using the
// *raw* projection so the handles don't inherit the scene's TAA jitter. Translate and rotate act on
// world axes; scale acts on the object's *local* basis (Transform.Scale is local — a world-axis
// scale of a rotated object isn't representable by it). See Docs/Documentation/Gizmos.md.
internal sealed class TransformGizmo
{
    private enum Axis { None, X, Y, Z }

    private enum Mode { Translate, Rotate, Scale }

    // Apparent handle length as a fraction of distance-to-camera — keeps the gizmo a roughly
    // constant on-screen size regardless of how far the selection is (a fixed world length would
    // shrink to nothing when zoomed out and swamp the screen up close).
    private const float HandleScreenFraction = 0.1f;

    private const float PickPixels      = 10f;  // cursor-to-handle distance that counts as a hover
    private const float LineThickness   = 5f;
    private const float ArrowPixels     = 13f;
    private const float BoxPixels       = 10f;  // scale-mode tip square
    private const float CentreDotPixels = 3.5f;
    private const float RingSegments    = 48;
    private const float ScaleSensitivity = 0.01f; // per-pixel fractional change when dragging scale
    private const float MinScale         = 1e-3f;

    // Rotate feel. The drag maps the mouse to a rotation via the *linear* (first-order)
    // approximation of the angle-around-centre map, frozen at the grab point — see
    // GizmoMath.RotationAngleDelta / ApplyRotateDrag for why (the exact atan2 map decelerates
    // drastically as a straight drag pulls away from the centre). Gain 1 keeps the initial
    // sensitivity identical to that exact map; the radius floor stops a grab near the centre from
    // becoming hypersensitive.
    private const float RotateGain           = 1f;
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
        var io       = ImGui.GetIO();
        var viewport = io.DisplaySize;
        var viewProj = camera.GetViewMatrix() * camera.GetProjectionMatrixRaw();

        if (!Project(origin, viewProj, viewport, out var oScreen))
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
        AxisDirections(t, isScale, axes);

        Span<Vector2> ends    = stackalloc Vector2[3];
        Span<bool>    visible = stackalloc bool[3];
        for (var i = 0; i < 3; i++)
            visible[i] = Project(origin + axes[i] * worldLen, viewProj, viewport, out ends[i]);

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
                if (isScale) 
                    ApplyScaleDrag(t, mouse);
                else         
                    ApplyTranslateDrag(t, mouse);
            }
            else 
                _drag = Axis.None;
        }

        DrawLinearHandles(oScreen, ends, visible, isScale);
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
        var scaled      = MathF.Max(MinScale, GetComponent(_dragStartScale, _dragAxisIndex) * factor);

        t.Scale = WithComponent(_dragStartScale, _dragAxisIndex, scaled);
    }

    // ---- Rotate: three world-axis rings -------------------------------------------------------
    private void RotateMode(Transform t, ImGuiIOPtr io, Camera camera, Vector3 origin, Vector2 oScreen,
        float worldLen, Matrix4x4 viewProj, Vector2 viewport)
    {
        Span<Vector3> axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        var mouse = io.MousePos;

        // Hover: nearest projected ring within the pick radius (each ring sampled to a polyline so
        // its perspective ellipse is hit-tested/drawn correctly, not approximated as a flat circle).
        if (_drag == Axis.None)
        {
            _hover = Axis.None;
            if (!io.WantCaptureMouse)
            {
                var best = Widgets.Scale(PickPixels);
                for (var i = 0; i < 3; i++)
                {
                    var d = DistanceToRing(mouse, origin, axes[i], worldLen, viewProj, viewport);
                    
                    if (!(d < best)) continue;
                    
                    best = d; 
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

        DrawRings(origin, axes, worldLen, viewProj, viewport, oScreen);
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
        var phi = RotationAngleDelta(_dragRotRadialHat, _dragRotInvRadius, mouse - _dragStartMouse, _dragRotSign, RotateGain);
        t.SetRotation(ComposeWorldRotation(_dragStartRot, _dragRotAxis, phi));
    }

    // ---- shared hit-test ----------------------------------------------------------------------
    private static Axis NearestHandle(Vector2 mouse, Vector2 oScreen, ReadOnlySpan<Vector2> ends,
        ReadOnlySpan<bool> visible, bool imGuiWantsMouse)
    {
        // Don't hijack the cursor when an ImGui panel (Outliner/Properties) wants it — otherwise a
        // handle behind a panel would steal that panel's clicks.
        if (imGuiWantsMouse) 
            return Axis.None;

        var hover = Axis.None;
        var best  = Widgets.Scale(PickPixels);
        for (var i = 0; i < 3; i++)
        {
            if (!visible[i]) continue;
            
            var d = DistanceToSegment(mouse, oScreen, ends[i]);
            
            if (!(d < best)) continue;
            
            best = d; 
            hover = (Axis)(i + 1);
        }
        return hover;
    }

    // ---- drawing ------------------------------------------------------------------------------
    private void DrawLinearHandles(Vector2 oScreen, ReadOnlySpan<Vector2> ends, ReadOnlySpan<bool> visible, bool isScale)
    {
        var dl = ImGui.GetForegroundDrawList();
        for (var i = 0; i < 3; i++)
        {
            if (!visible[i]) continue;
            
            var color = ImGui.GetColorU32(ColorFor((Axis)(i + 1)));
            if (isScale) 
                DrawScaleHandle(dl, oScreen, ends[i], color);
            else         
                DrawArrow(dl, oScreen, ends[i], color);
        }
        
        dl.AddCircleFilled(oScreen, Widgets.Scale(CentreDotPixels), ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f)));
    }

    private void DrawRings(Vector3 origin, ReadOnlySpan<Vector3> axes, float worldLen, Matrix4x4 viewProj, Vector2 viewport, Vector2 oScreen)
    {
        var dl = ImGui.GetForegroundDrawList();
        const int n = (int)RingSegments;
        Span<Vector2> pts = stackalloc Vector2[(int)RingSegments];

        for (var a = 0; a < 3; a++)
        {
            var (u, v) = PlaneBasis(axes[a]);
            var count  = 0;
            for (var s = 0; s < n; s++)
            {
                var theta = s / RingSegments * MathF.Tau;
                var world = origin + (u * MathF.Cos(theta) + v * MathF.Sin(theta)) * worldLen;
                if (Project(world, viewProj, viewport, out var p)) 
                    pts[count++] = p;
            }
            
            if (count < 2) continue;

            var color = ImGui.GetColorU32(ColorFor((Axis)(a + 1)));
            for (var s = 0; s < count; s++)
                dl.AddLine(pts[s], pts[(s + 1) % count], color, Widgets.Scale(LineThickness * 0.6f));
        }
        dl.AddCircleFilled(oScreen, Widgets.Scale(CentreDotPixels), ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f)));
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

    private static void DrawScaleHandle(ImDrawListPtr dl, Vector2 from, Vector2 to, uint color)
    {
        dl.AddLine(from, to, color, Widgets.Scale(LineThickness));
        var h = Widgets.Scale(BoxPixels) * 0.5f;
        dl.AddRectFilled(new Vector2(to.X - h, to.Y - h), new Vector2(to.X + h, to.Y + h), color);
    }

    // ---- geometry helpers ---------------------------------------------------------------------

    // Translate/rotate use world axes; scale uses the object's local basis (its world-rotated
    // X/Y/Z), so dragging a handle scales that local axis — the only thing Transform.Scale can express.
    private static void AxisDirections(Transform t, bool isScale, Span<Vector3> dest)
    {
        if (!isScale)
        {
            dest[0] = Vector3.UnitX; dest[1] = Vector3.UnitY; dest[2] = Vector3.UnitZ;
            return;
        }

        var m = t.WorldMatrix;
        dest[0] = SafeNormalize(new Vector3(m.M11, m.M12, m.M13), Vector3.UnitX);
        dest[1] = SafeNormalize(new Vector3(m.M21, m.M22, m.M23), Vector3.UnitY);
        dest[2] = SafeNormalize(new Vector3(m.M31, m.M32, m.M33), Vector3.UnitZ);
    }

    // An orthonormal (u, v) spanning the plane perpendicular to axis — the ring for that axis lives
    // in it. Picks a reference not near-parallel to the axis so the cross products stay well-defined.
    private static (Vector3 u, Vector3 v) PlaneBasis(Vector3 axis)
    {
        var reference = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        var u = Vector3.Normalize(Vector3.Cross(axis, reference));
        var v = Vector3.Cross(axis, u);
        return (u, v);
    }

    private static float DistanceToRing(Vector2 mouse, Vector3 origin, Vector3 axis, float worldLen, Matrix4x4 viewProj, Vector2 viewport)
    {
        var (u, v) = PlaneBasis(axis);
        const int n = (int)RingSegments;

        var best = float.MaxValue;
        var havePrev = false;
        var prev = default(Vector2);
        var first = default(Vector2);
        var haveFirst = false;

        for (var s = 0; s <= n; s++)
        {
            var theta = (s % n) / RingSegments * MathF.Tau;
            var world = origin + (u * MathF.Cos(theta) + v * MathF.Sin(theta)) * worldLen;
            if (!Project(world, viewProj, viewport, out var p))
            {
                havePrev = false; 
                continue;
            }

            if (!haveFirst)
            {
                first = p;
                haveFirst = true;
            }
            if (havePrev) 
                best = MathF.Min(best, DistanceToSegment(mouse, prev, p));
            prev = p;
            havePrev = true;
        }
        // close the loop
        if (haveFirst && havePrev) best = MathF.Min(best, DistanceToSegment(mouse, prev, first));
        return best;
    }

    // World-space rotation: apply `start`, then spin the whole thing about the world `axis`. In
    // System.Numerics, Vector3.Transform(v, a*b) == Transform(Transform(v, b), a), so the world
    // delta *pre*-multiplies the start orientation. (Pinned by TransformGizmoTests — the object-frame
    // order, start*delta, fails ComposeWorldRotation_AppliesTheDeltaInTheWorldFrame.)
    internal static Quaternion ComposeWorldRotation(Quaternion start, Vector3 axis, float angleRad)
    {
        var delta = Quaternion.CreateFromAxisAngle(SafeNormalize(axis, Vector3.UnitY), angleRad);
        return Quaternion.Normalize(delta * start);
    }

    // Rotation angle (radians) for a rotate drag, as the *linear* first-order approximation of the
    // exact angle-around-centre map, frozen at the grab point. The exact map, atan2(cur−centre) −
    // atan2(grab−centre), tracks a cursor circling the centre perfectly but decelerates hard on a
    // straight drag: the effective radius grows as the cursor pulls away, so each pixel turns less
    // and less. Linearising at the grab keeps the *initial* rate and sign identical (the derivative
    // of atan2 along the tangent is cross(radialHat, ·)/radius) while staying constant-rate for the
    // straight drags people actually do. `radialHat` is the unit centre→grab direction and
    // `invRadius` = 1/|grab−centre|; the 2-D cross with the mouse delta is the signed tangential
    // distance. `sign` carries the screen→world handedness, `gain` is a feel multiplier.
    internal static float RotationAngleDelta(Vector2 radialHat, float invRadius, Vector2 mouseDelta, float sign, float gain)
    {
        var tangential = radialHat.X * mouseDelta.Y - radialHat.Y * mouseDelta.X; // 2-D cross
        return sign * gain * tangential * invRadius;
    }

    private Vector4 ColorFor(Axis axis)
    {
        if (_drag == axis || (_drag == Axis.None && _hover == axis))
            return new Vector4(1f, 0.85f, 0.20f, 1f); // highlight

        return axis switch
        {
            Axis.X => new Vector4(0.90f, 0.25f, 0.25f, 1f),
            Axis.Y => new Vector4(0.40f, 0.85f, 0.40f, 1f),
            _      => new Vector4(0.35f, 0.55f, 0.95f, 1f),
        };
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var len = v.Length();
        return len < 1e-6f ? fallback : v / len;
    }

    private static float GetComponent(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    private static Vector3 WithComponent(Vector3 v, int i, float value) => i switch
    {
        0 => v with { X = value },
        1 => v with { Y = value },
        _ => v with { Z = value },
    };

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
        if (lenSq < 1e-6f) 
            return Vector2.Distance(p, a);

        var tRaw = Vector2.Dot(p - a, ab) / lenSq;
        var t    = Math.Clamp(tRaw, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
