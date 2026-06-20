namespace Centauri.Rendering.Shadows;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Utils.Misc;
using Graphics.Resources;

public sealed class ShadowMapper : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    private ShadowArray _maps;
    private readonly GLShader _depth;

    public bool Active { get; private set; }
    public uint DepthTexture => _maps.DepthTexture;

    public Matrix4x4[] LightMatrices { get; private set; } = [];  // proj·view per cascade (numerics order = View*Proj)
    public float[]     SplitDepths   { get; private set; } = [];  // view-space far depth per cascade
    
    private int CascadeCount => Math.Clamp(_config.Shadows.CascadeCount, 1, _config.Shadows.MaxCascades);

    public ShadowMapper(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        _maps = new ShadowArray(gl, config.Shadows.Size, CascadeCount);
        _depth = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.vert"),
            PathResolver.Resolve("Assets/Shaders/Shadow/depth.frag"));
    }

    public void Render(Scene scene)
    {
        Active = false;
        if (!_config.Shadows.Enabled) return;

        // re-alloc on resolution OR cascade-count change
        if (_maps.Size != _config.Shadows.Size || _maps.Layers != CascadeCount)
        {
            _maps.Dispose();
            _maps = new ShadowArray(_gl, _config.Shadows.Size, CascadeCount);
        }

        if (scene.Lighting.DirectionalLights.Count == 0) return;

        var dir    = Vector3.Normalize(scene.Lighting.DirectionalLights[0].Direction);
        var camera = scene.Cameras.Active;

        ComputeCascades(camera, dir);   // fills LightMatrices + SplitDepths

        _gl.Disable(EnableCap.CullFace);
        for (var c = 0; c < CascadeCount; c++)
        {
            _maps.BindLayer(c);
            _depth.Use();
            _depth.SetUniform("uLightMatrix", LightMatrices[c]);

            foreach (var entity in scene.Entities)
            {
                if (!entity.Enabled || entity.Model is not { } model) 
                    continue;
                
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
        
        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Active = true;
    }
    private void ComputeCascades(Camera camera, Vector3 dir)
    {
        var n = CascadeCount;
        LightMatrices = new Matrix4x4[n];
        SplitDepths   = new float[n];

        var near   = _config.Camera.Near;
        var camFar = _config.Camera.Far;
        var far    = MathF.Min(_config.Shadows.Distance, camFar);   // shadow range = split max

        Span<Vector3> frustum = stackalloc Vector3[8];
        GetFrustumCorners(camera, frustum);

        var prevSplit = near;
        for (var c = 0; c < n; c++)
        {
            var split = CascadeSplit(c, n, near, far);
            SplitDepths[c] = split;

            Span<Vector3> slice = stackalloc Vector3[8];
            SliceCorners(frustum, (prevSplit - near) / (camFar - near),
                                  (split     - near) / (camFar - near), slice);

            LightMatrices[c] = FitCascade(slice, dir);
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
    private Matrix4x4 FitCascade(ReadOnlySpan<Vector3> corners, Vector3 dir)
    {
        // bounding sphere → box size is invariant to camera orientation
        var center = Vector3.Zero;
        foreach (var p in corners) 
            center += p;
        center /= 8f;

        var radius = 0f;
        foreach (var p in corners)
            radius = MathF.Max(radius, (p - center).Length());
        radius = MathF.Ceiling(radius * 16f) / 16f;             // quantize radius — removes size shimmer

        var up   = MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(center - dir * radius, center, up);

        // snap box center to whole-texel increments — this is what stops shadow swimming
        var texelSize  = (radius * 2f) / _config.Shadows.Size;
        var   centerLS   = Vector3.Transform(center, view);
        
        centerLS.X = MathF.Floor(centerLS.X / texelSize) * texelSize;
        centerLS.Y = MathF.Floor(centerLS.Y / texelSize) * texelSize;

        // z extent from corners + pull-back so occluders behind the slice still cast
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in corners)
        {
            var z = Vector3.Transform(p, view).Z;
            minZ = MathF.Min(minZ, z);
            maxZ = MathF.Max(maxZ, z);
        }
        var zPad = radius;

        var proj = Matrix4x4.CreateOrthographicOffCenter(
            centerLS.X - radius, centerLS.X + radius,
            centerLS.Y - radius, centerLS.Y + radius,
            -maxZ - zPad, -minZ + zPad
        );

        return view * proj;   // numerics order; GLSL: uLightMatrix * pos
    }

    public void Dispose()
    {
        _maps.Dispose();
        _depth.Dispose();
    }
}