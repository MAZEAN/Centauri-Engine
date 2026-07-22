namespace Centauri.UI.Gizmos;

using System.Numerics;

using World;

// Pure geometry for the transform gizmo — projection, screen-space hit-tests, and the rotation
// math. No ImGui, no GL, no gizmo state: everything here is a static function of its arguments, so
// it's unit-tested directly (see Centauri.Tests/UI/TransformGizmoTests.cs). TransformGizmo (the
// interaction/state) and GizmoDraw (the rendering) both build on this.
internal static class GizmoMath
{
    // Ring tessellation — shared by the rotate hit-test (DistanceToRing) and the rotate draw
    // (GizmoDraw.Rings) so the polyline they test against and the one drawn are the same.
    public const int RingSegments = 48;

    // Row-vector projection matching Camera.ScreenPointToRay: clip = point * (view*proj), then the
    // usual perspective divide and NDC→screen flip. Returns false when the point is at/behind the
    // camera plane (w <= 0), where the divide is meaningless.
    public static bool Project(Vector3 world, Matrix4x4 viewProj, Vector2 viewport, out Vector2 screen)
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

    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        if (lenSq < 1e-6f)
            return Vector2.Distance(p, a);

        var tRaw = Vector2.Dot(p - a, ab) / lenSq;
        var t    = Math.Clamp(tRaw, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    // Nearest screen distance from `mouse` to the projected ring for `axis` (radius `worldLen`
    // about `origin`), sampled to a polyline so its perspective ellipse is measured correctly
    // rather than approximated as a flat circle. Segments crossing behind the camera are skipped.
    public static float DistanceToRing(Vector2 mouse, Vector3 origin, Vector3 axis, float worldLen, Matrix4x4 viewProj, Vector2 viewport)
    {
        var (u, v) = PlaneBasis(axis);

        var best      = float.MaxValue;
        var havePrev  = false;
        var haveFirst = false;
        var prev      = default(Vector2);
        var first     = default(Vector2);

        for (var s = 0; s <= RingSegments; s++)
        {
            var theta = s % RingSegments / (float)RingSegments * MathF.Tau;
            var world = origin + (u * MathF.Cos(theta) + v * MathF.Sin(theta)) * worldLen;
            if (!Project(world, viewProj, viewport, out var p))
            {
                havePrev = false;
                continue;
            }

            if (!haveFirst) { first = p; haveFirst = true; }
            if (havePrev) best = MathF.Min(best, DistanceToSegment(mouse, prev, p));
            prev = p;
            havePrev = true;
        }

        if (haveFirst && havePrev) best = MathF.Min(best, DistanceToSegment(mouse, prev, first)); // close the loop
        return best;
    }

    // An orthonormal (u, v) spanning the plane perpendicular to axis — the ring for that axis lives
    // in it. Picks a reference not near-parallel to the axis so the cross products stay well-defined.
    public static (Vector3 u, Vector3 v) PlaneBasis(Vector3 axis)
    {
        var reference = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        var u = Vector3.Normalize(Vector3.Cross(axis, reference));
        var v = Vector3.Cross(axis, u);
        return (u, v);
    }

    // Translate/rotate use world axes; scale uses the object's local basis (its world-rotated
    // X/Y/Z), so dragging a handle scales that local axis — the only thing Transform.Scale can express.
    public static void AxisDirections(Transform t, bool isScale, Span<Vector3> dest)
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

    // World-space rotation: apply `start`, then spin the whole thing about the world `axis`. In
    // System.Numerics, Vector3.Transform(v, a*b) == Transform(Transform(v, b), a), so the world
    // delta *pre*-multiplies the start orientation. (Pinned by TransformGizmoTests — the object-frame
    // order, start*delta, fails ComposeWorldRotation_AppliesTheDeltaInTheWorldFrame.)
    public static Quaternion ComposeWorldRotation(Quaternion start, Vector3 axis, float angleRad)
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
    public static float RotationAngleDelta(Vector2 radialHat, float invRadius, Vector2 mouseDelta, float sign, float gain)
    {
        var tangential = radialHat.X * mouseDelta.Y - radialHat.Y * mouseDelta.X; // 2-D cross
        return sign * gain * tangential * invRadius;
    }

    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var len = v.Length();
        return len < 1e-6f ? fallback : v / len;
    }

    public static float GetComponent(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    public static Vector3 WithComponent(Vector3 v, int i, float value) => i switch
    {
        0 => v with { X = value },
        1 => v with { Y = value },
        _ => v with { Z = value },
    };
}
