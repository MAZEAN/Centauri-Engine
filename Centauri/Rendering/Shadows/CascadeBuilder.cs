namespace Centauri.Rendering.Shadows;

using System.Numerics;

using Config;
using World;
using Utils.Geometry;

public struct Cascade
{
    public Matrix4x4 Matrix;      // lightView * lightProj  (GLSL: uLightMatrix * pos)
    public float     SplitDepth;  // view-space far depth — used for cascade selection
    public Vector3   Center;      // world-space slice center  (depth-pass culling, Step 2)
    public float     Radius;      // bounding-sphere radius    (depth-pass culling, Step 2)
    public float     DepthRange;  // world-space extent of the light-space Z range (farZ - nearZ) —
    // lets PCSS convert a depth-space blocker distance to world units
}

public sealed class CascadeBuilder
{
    private const float UpThreshold = 0.99f;   // switch up-vector when the sun is ~vertical
    private const float RadiusSnap  = 16f;     // quantize sphere radius to 1/16 units (size-shimmer guard)
    private const float ZEpsilon    = 1f;
    private const float ZSnap       = 1f;      // quantize ortho depth range to whole units (depth-precision guard)

    // The X/Y stability snap grid is deliberately coarser than one render texel. Snapping to
    // exactly a texel (the textbook formula) still re-snaps on almost every frame of continuous
    // camera motion, since a texel is tiny relative to typical per-frame movement — it stops
    // sub-texel shimmer but not frame-to-frame flicker. Widening the snap grid to a multiple of
    // the texel fixes that without costing any resolution or blur: the render target size is
    // untouched, only the box's position is quantized more coarsely. FitCascade compensates by
    // growing the radius by one coarse step so the box can never clip content despite landing up
    // to that much off the ideal center.
    private const float StabilitySnapScale = 16f;

    private readonly AppConfig _config;

    public CascadeBuilder(AppConfig config) => _config = config;

    private int CascadeCount => Math.Clamp(_config.Shadows.CascadeCount, 1, _config.Shadows.MaxCascades);

    // Builds texel-snapped cascades for the given light direction. Reuses `reuse` when it's
    // already the right length so the steady state allocates nothing.
    //
    // `resolutionOf(index)` returns the physical resolution cascade `index` actually renders
    // at — the texel-snap grid below needs to match that real resolution, not assume every
    // cascade shares cascade 0's. Cascades other than 0 share one lower-resolution tier (see
    // ShadowConfig.FarCascadeScale/ShadowMapper), so without this the snap grid is calibrated
    // as if that tier were never actually smaller — silently discarding the stability its lower
    // resolution should provide, since a coarser render target can tolerate the fitted box
    // landing on the same texel across a wider range of camera positions.
    public Cascade[] Build(Camera camera, Vector3 dir, BoundingBox sceneBounds,
        Func<int, float> resolutionOf, Cascade[] reuse)
    {
        var n = CascadeCount;
        var cascades = reuse.Length == n ? reuse : new Cascade[n];

        var near   = _config.Camera.Near;
        var camFar = _config.Camera.Far;
        var far    = MathF.Min(_config.Shadows.Distance, camFar);

        Span<Vector3> frustum = stackalloc Vector3[8];
        Span<Vector3> slice   = stackalloc Vector3[8];

        GetFrustumCorners(camera, frustum);

        var prevSplit = near;
        for (var c = 0; c < n; c++)
        {
            var split = CascadeSplit(c, n, near, far, _config.Shadows.SplitLambda);
            var invRange = 1f / (camFar - near);

            SliceCorners(frustum, (prevSplit - near) * invRange, (split - near) * invRange, slice);

            cascades[c] = FitCascade(slice, dir, split, sceneBounds, resolutionOf(c));
            prevSplit = split;
        }

        return cascades;
    }

    // world-space corners of the camera's full frustum, unprojected from NDC. Uses the RAW
    // (unjittered) projection — TAA's per-frame sub-pixel jitter (see Camera.JitterNdc) is a
    // display-only technique with no bearing on which world-space slice the shadow needs to
    // cover, so using the jittered matrix here would wobble the fitted cascades every single
    // frame purely from jitter, even with a genuinely static camera and light — exactly the kind
    // of continuous, imperceptible-per-frame change the texel/Z snap and ShadowCache are meant to
    // collapse, except jitter offers no such fixed grid to collapse onto since it's not a
    // translation within one.
    private static void GetFrustumCorners(Camera camera, Span<Vector3> corners)
    {
        Matrix4x4.Invert(camera.GetViewMatrix() * camera.GetProjectionMatrixRaw(), out var invVP);

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
    private static float CascadeSplit(int c, int n, float near, float far, float splitLambda)
    {
        var p   = (c + 1) / (float)n;
        var log = near * MathF.Pow(far / near, p);
        var uni = near + (far - near) * p;

        return splitLambda * log + (1f - splitLambda) * uni;
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
    private Cascade FitCascade(ReadOnlySpan<Vector3> corners, Vector3 dir, float splitDepth,
        BoundingBox sceneBounds, float resolution)
    {
        var center = Vector3.Zero;
        foreach (var p in corners)
            center += p;
        center /= 8f;

        // Quantize outward (never shrink — the box must still contain the slice) so texelSize
        // below, and therefore the centerLS snap grid, only steps discretely instead of drifting
        // continuously with the camera's exact distance/FOV each frame — without this the center
        // snap alone doesn't stop shimmer, since the grid it's snapping to is itself unstable.
        var radius = ComputeRadius(corners, center);
        radius = MathF.Ceiling(radius / RadiusSnap) * RadiusSnap;

        var up = MathF.Abs(dir.Y) > UpThreshold ? Vector3.UnitZ : Vector3.UnitY;

        // Build the light's view from a FIXED point (world origin), not re-targeted at `center`
        // each frame — that was the actual bug in the old texel snap. Re-targeting at center
        // makes center transform to ~(0,0,-radius) in its own view by definition (X/Y ~0 from
        // floating-point rounding alone, regardless of where center is in the world), so
        // flooring that was a no-op; and even correcting just the X/Y bounds by a delta still
        // left the view's own translation (eye = center - dir*radius) tracking center's raw,
        // un-snapped position, which — because that translation shifts the world-to-view-space
        // mapping along the light's own depth axis, i.e. the very axis nearZ/farZ are measured
        // against, even though "shifting along the view's own forward axis doesn't change X/Y"
        // — silently reintroduced continuous drift into every downstream Z value, and therefore
        // into the final Matrix, on every single frame of camera motion however small.
        //
        // With a fixed-origin view, every quantity below (centerRef, sliceMinZ/MaxZ, casterMaxZ)
        // is measured against the SAME stable frame every frame, so texel-snapping X/Y and
        // whole-unit-snapping near/far actually collapse nearby camera positions onto identical
        // matrices, instead of merely producing numbers that individually look stable while the
        // frame they're measured against keeps moving underneath them.
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, dir, up);

        // snapTexel is a coarse multiple of the real render texel — see StabilitySnapScale.
        // Flooring to it can leave the box up to one snapTexel short of the ideal center, so the
        // ortho bounds below use a padded boxRadius to guarantee the box still fully contains the
        // slice. `radius` itself stays the true render radius: it's returned on the Cascade and
        // feeds the shader's world-texel-size (Radius*2/resolution) for PCSS penumbra math, which
        // must reflect the actual render target, not this padding.
        var renderTexel = (radius * 2f) / resolution;
        var snapTexel = renderTexel * StabilitySnapScale;
        var boxRadius = radius + snapTexel;

        var centerLS = Vector3.Transform(center, view);
        centerLS.X = MathF.Floor(centerLS.X / snapTexel) * snapTexel;
        centerLS.Y = MathF.Floor(centerLS.Y / snapTexel) * snapTexel;

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
            centerLS.X - boxRadius, centerLS.X + boxRadius,
            centerLS.Y - boxRadius, centerLS.Y + boxRadius,
            nearZ, farZ);

        return new Cascade
        {
            Matrix     = view * proj,
            SplitDepth = splitDepth,
            Center     = center,
            Radius     = radius,
            DepthRange = farZ - nearZ,
        };
    }

    private static float LightSpaceMaxZ(BoundingBox bounds, Matrix4x4 view)
    {
        var maxZ = float.MinValue;
        foreach (var corner in bounds.GetBoxCorners())
            maxZ = MathF.Max(maxZ, Vector3.Transform(corner, view).Z);

        return maxZ;
    }

    private static float ComputeRadius(ReadOnlySpan<Vector3> corners, Vector3 center)
    {
        var radiusSq = 0f;
        foreach (var p in corners)
        {
            radiusSq = MathF.Max(radiusSq,
                Vector3.DistanceSquared(p, center));
        }

        return MathF.Sqrt(radiusSq);
    } 
}
