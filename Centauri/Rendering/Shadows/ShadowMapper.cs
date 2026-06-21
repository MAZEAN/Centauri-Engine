namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Utils.Misc;
using Graphics.Resources;
using Utils.Geometry;

public sealed class ShadowMapper : IDisposable
{
    private const float UpThreshold = 0.99f;   // switch up-vector when the sun is ~vertical
    private const float RadiusSnap  = 16f;     // quantize sphere radius to 1/16 units (size-shimmer guard)
    private const float ZEpsilon = 1f;
    private const float ZSnap    = 1f;         // quantize ortho depth range to whole units (depth-precision shimmer guard)
    
    private readonly GL _gl;
    private readonly AppConfig _config;
    private ShadowArray _maps;
    private readonly GLShader _depth;
    private readonly Frustum _cull = new();

    public bool Active { get; private set; }
    public uint DepthTexture => _maps.DepthTexture;
    
    public Cascade[] Cascades { get; private set; } = [];
    
    private int CascadeCount => Math.Clamp(_config.Shadows.CascadeCount, 1, _config.Shadows.MaxCascades);

    public ShadowMapper(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        // pre-allocate every layer up front — cascade-count changes never re-alloc (no frame stall)
        _maps = new ShadowArray(gl, config.Shadows.Size, config.Shadows.MaxCascades);
        _depth = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.frag"));
    }

    public void Render(Scene scene, ref FrameStats stats)
    {
        stats.ShadowCasters = 0;
        stats.ShadowCulled  = 0;

        Active = false;
        if (!_config.Shadows.Enabled) return;

        if (_maps.Size != _config.Shadows.Size)
        {
            _maps.Dispose();
            _maps = new ShadowArray(_gl, _config.Shadows.Size, _config.Shadows.MaxCascades);
        }

        if (scene.Lighting.DirectionalLights.Count == 0) return;

        var dir         = Vector3.Normalize(scene.Lighting.DirectionalLights[0].Direction);
        var camera      = scene.Cameras.Active;
        var sceneBounds = ComputeSceneBounds(scene);

        ComputeCascades(camera, dir, sceneBounds);

        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(2.5f, 4f);        // slope-scaled depth bias in hardware (per-cascade correct, no smear)
        
        for (var c = 0; c < Cascades.Length; c++)
        {
            _maps.BindLayer(c);
            _depth.Use();
            _depth.SetUniform("uLightMatrix", Cascades[c].Matrix);
            _cull.Update(Cascades[c].Matrix);

            foreach (var entity in scene.Entities)
            {
                if (!entity.Enabled || entity.Model is not { } model)
                    continue;

                if (!_cull.IsVisibleAABB(entity.GetWorldBounds()))
                {
                    stats.ShadowCulled++;
                    continue;
                }

                stats.ShadowCasters++;
                _depth.SetUniform("uModel", entity.Transform.WorldMatrix);
                foreach (var mesh in model.Meshes)
                {
                    mesh.Bind();
                    unsafe
                    {
                        _gl.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                            DrawElementsType.UnsignedInt, (void*)0);
                    }
                }
            }
        }

        _gl.PolygonOffset(0f, 0f);
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Active = true;
    }
    
    private void ComputeCascades(Camera camera, Vector3 dir, BoundingBox sceneBounds)
    {
        var n = CascadeCount;
        if (Cascades.Length != n)
            Cascades = new Cascade[n];

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
                (split     - near) / (camFar - near), slice);

            Cascades[c] = FitCascade(slice, dir, split, sceneBounds);
            prevSplit = split;
        }
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
    
    private static BoundingBox ComputeSceneBounds(Scene scene)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var e in scene.Entities)
        {
            if (!e.Enabled || e.Model is null) continue;
            var b = e.GetWorldBounds();
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
        }

        return min.X <= max.X ? new BoundingBox(min, max)
            : new BoundingBox(Vector3.Zero, Vector3.Zero);   // no casters
    }

    public void Dispose()
    {
        _maps.Dispose();
        _depth.Dispose();
    }
}