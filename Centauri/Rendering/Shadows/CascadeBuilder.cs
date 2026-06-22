namespace Centauri.Rendering.Shadows;

using System.Numerics;

using Config;
using World;
using Utils.Geometry;

// Pure cascade-fitting math for CSM — no GL. Splits the camera frustum (PSSM),
// fits a stable, texel-snapped ortho box per slice (bounding-sphere method), and
// extends the depth range toward the closest caster. Lives apart from ShadowMapper
// so the bias/cascade tuning is testable without a GL context.
public sealed class CascadeBuilder
{
    private const float UpThreshold = 0.99f;   // switch up-vector when the sun is ~vertical
    private const float RadiusSnap  = 16f;     // quantize sphere radius to 1/16 units (size-shimmer guard)
    private const float ZEpsilon    = 1f;
    private const float ZSnap       = 1f;      // quantize ortho depth range to whole units (depth-precision guard)

    private readonly AppConfig _config;

    public CascadeBuilder(AppConfig config) => _config = config;

    private int CascadeCount => Math.Clamp(_config.Shadows.CascadeCount, 1, _config.Shadows.MaxCascades);

    // Builds texel-snapped cascades for the given light direction. Reuses `reuse` when it's
    // already the right length so the steady state allocates nothing.
    public Cascade[] Build(Camera camera, Vector3 dir, BoundingBox sceneBounds, Cascade[] reuse)
    {
        var n = CascadeCount;
        var cascades = reuse.Length == n ? reuse : new Cascade[n];

        var near   = _config.Camera.Near;
        var camFar = _config.Camera.Far;
        var far    = MathF.Min(_config.Shadows.Distance, camFar);

        Span<Vector3> frustum = stackalloc Vector3[8];
        GetFrustumCorners(camera, frustum);

        var prevSplit = near;
        for (var c = 0; c < n; c++)
        {
            var split = CascadeSplit(c, n, near, far);

            Span<Vector3> slice = stackalloc Vector3[8];
            SliceCorners(frustum, (prevSplit - near) / (camFar - near),
                (split - near) / (camFar - near), slice);

            cascades[c] = FitCascade(slice, dir, split, sceneBounds);
            prevSplit = split;
        }

        return cascades;
    }

    // world-space corners of the camera's full frustum, unprojected from NDC
    private static void GetFrustumCorners(Camera camera, Span<Vector3> corners)
    {
        Matrix4x4.Invert(camera.GetViewMatrix() * camera.GetProjectionMatrix(), out var invVP);

        var k = 0;
        for (var x = 0; x < 2; x++)
            for (var y = 0; y < 2; y++)
                for (var z = 0; z < 2; z++)
                {
                    var ndc = new Vector4(x * 2 - 1, y * 2 - 1, z, 1f);   // .NET proj: near z=0, far z=1
                    var w   = Vector4.Transform(ndc, invVP);
                    corners[k++] = new Vector3(w.X, w.Y, w.Z) / w.W;
                }
    }

    // PSSM: blend logarithmic and uniform split distances
    private float CascadeSplit(int c, int n, float near, float far)
    {
        var p   = (c + 1) / (float)n;
        var log = near * MathF.Pow(far / near, p);
        var uni = near + (far - near) * p;

        return _config.Shadows.SplitLambda * log + (1f - _config.Shadows.SplitLambda) * uni;
    }

    // interpolate the slice's 8 corners along the frustum edges (z is linear along edges)
    private static void SliceCorners(ReadOnlySpan<Vector3> frustum, float t0, float t1, Span<Vector3> slice)
    {
        for (var i = 0; i < 4; i++)
        {
            var nearCorner = frustum[i * 2 + 0];
            var edge       = frustum[i * 2 + 1] - nearCorner;   // near→far corner pair
            slice[i + 0] = nearCorner + edge * t0;              // slice near
            slice[i + 4] = nearCorner + edge * t1;              // slice far
        }
    }

    // fit a stable, texel-snapped ortho box around the slice (bounding-sphere method)
    private Cascade FitCascade(ReadOnlySpan<Vector3> corners, Vector3 dir, float splitDepth, BoundingBox sceneBounds)
    {
        var center = Vector3.Zero;
        foreach (var p in corners)
            center += p;
        center /= 8f;

        var radius = 0f;
        foreach (var p in corners)
            radius = MathF.Max(radius, (p - center).Length());
        radius = MathF.Ceiling(radius * RadiusSnap) / RadiusSnap;

        var up   = MathF.Abs(dir.Y) > UpThreshold ? Vector3.UnitZ : Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(center - dir * radius, center, up);

        var texelSize = (radius * 2f) / _config.Shadows.Size;
        var centerLS  = Vector3.Transform(center, view);

        centerLS.X = MathF.Floor(centerLS.X / texelSize) * texelSize;
        centerLS.Y = MathF.Floor(centerLS.Y / texelSize) * texelSize;

        float sliceMinZ = float.MaxValue, sliceMaxZ = float.MinValue;
        foreach (var p in corners)
        {
            var z = Vector3.Transform(p, view).Z;
            sliceMinZ = MathF.Min(sliceMinZ, z);
            sliceMaxZ = MathF.Max(sliceMaxZ, z);
        }

        // near plane extends toward the light to the closest caster (so occluders above
        // the slice still cast); far plane stays at the slice's farthest receiver
        var casterMaxZ = LightSpaceMaxZ(sceneBounds, view);
        var nearZ = -(MathF.Max(sliceMaxZ, casterMaxZ) + ZEpsilon);
        var farZ  = -(sliceMinZ - ZEpsilon);

        // snap outward to a fixed grid so the depth range steps discretely instead of
        // swinging every frame with the light — keeps stored depth precision stable
        nearZ = MathF.Floor(nearZ / ZSnap) * ZSnap;
        farZ  = MathF.Ceiling(farZ / ZSnap) * ZSnap;

        var proj = Matrix4x4.CreateOrthographicOffCenter(
            centerLS.X - radius, centerLS.X + radius,
            centerLS.Y - radius, centerLS.Y + radius,
            nearZ, farZ);

        return new Cascade
        {
            Matrix     = view * proj,
            SplitDepth = splitDepth,
            Center     = center,
            Radius     = radius,
        };
    }

    private static float LightSpaceMaxZ(BoundingBox bounds, Matrix4x4 view)
    {
        var maxZ = float.MinValue;
        foreach (var corner in bounds.GetBoxCorners())
            maxZ = MathF.Max(maxZ, Vector3.Transform(corner, view).Z);

        return maxZ;
    }
}
