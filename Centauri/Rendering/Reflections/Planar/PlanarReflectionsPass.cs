namespace Centauri.Rendering.Reflections.Planar;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Renderers;
using Culling;
using Targets;
using Utils.Misc;
using Utils.Geometry;

// Planar reflections: renders the scene a second time from a camera mirrored about a
// horizontal plane (y = PlaneHeight) into an off-screen target. The SSR resolve then samples
// this on up-facing floor pixels at that height, giving a complete, noise-free reflection for
// a flat reflector (water / wet ground) — the one case screen-space SSR and the direction-
// sampled probe can't cover. Rendered at half resolution by default and reusing the main
// view's shadow cascades (world space), so it adds no shadow pass and only a fraction of a
// forward pass in fill.
//
// v1 scope: one global horizontal plane; no oblique near-plane clip (geometry below the plane
// can leak, but there's nothing under a ground plane in practice); winding is flipped for the
// mirrored pass so back-face culling still keeps the correct faces.
public sealed class PlanarReflectionPass : IDisposable
{
    private const float VisibilityMargin = 0.5f;

    private readonly GL _gl;
    private readonly PlanarReflectionConfig _config;
    private readonly MainRenderer _main;
    private readonly SkyboxRenderer _skybox;
    
    // Holds the mirrored-frustum visible set; queries the main CullingSystem's grid rather than
    // owning/rebuilding its own
    private readonly CullingSystem _mirrorCulling = new();
    private readonly Frustum _mirrorFrustum = new();
    
    private readonly RenderTarget _target;

    private float _resolvedHeight;
    private readonly uint _samples;
    private uint _msaaFbo, _msaaColor, _msaaDepth;

    public uint  ReflectionTexture => _target.ColorTextures[0];
    public bool  Enabled     => _config.Enabled;
    public float PlaneHeight => _resolvedHeight;
    public float Intensity   => _config.Intensity;
    public float Distortion  => _config.Distortion;
    public float Blur        => _config.Blur;

    public PlanarReflectionPass(GL gl, PlanarReflectionConfig config, MainRenderer main,
        SkyboxRenderer skybox, uint width, uint height, uint samples)
    {
        _gl = gl;
        _config = config;
        _main = main;
        _skybox = skybox;
        _samples = Math.Max(1u, samples);

        var div = _config.HalfResolution ? 2u : 1u;
        _target = new RenderTarget(gl, Math.Max(1u, width / div), Math.Max(1u, height / div),
            [InternalFormat.Rgba16f], withDepth: false, filter: GLEnum.Linear);
        AllocateMsaa();
    }

    public void Resize(uint width, uint height)
    {
        var div = _config.HalfResolution ? 2u : 1u;
        _target.Resize(Math.Max(1u, width / div), Math.Max(1u, height / div));
        
        DestroyMsaa();
        AllocateMsaa();
    }

    public void Render(Scene scene, float deltaTime, CullingSystem mainCulling, ref FrameStats stats)
    {
        if (!_config.Enabled) return;
        
        using var _ = Profiling.Tracy.Scope("PlanarReflectionPass.Render");

        var camera = scene.Cameras.Active;
        var h = ResolvePlaneHeight(scene);
        _resolvedHeight = h;
        
        if (!PlaneInFrustum(camera, h)) return;
        
        var reflect = new Matrix4x4(
            1f,     0f, 0f, 0f,
            0f,    -1f, 0f, 0f,
            0f,     0f, 1f, 0f,
            0f, 2f * h, 0f, 1f);

        var reflView = reflect * camera.GetViewMatrix();
        var reflPos  = new Vector3(camera.Position.X, 2f * h - camera.Position.Y, camera.Position.Z);
        var proj     = camera.GetProjectionMatrix();
        
        // Cull against the mirrored view, not the main camera's — an entity outside the
        // reflected frustum can't appear in the reflection even if it's visible directly.
        _mirrorFrustum.Update(reflView * proj);
        _mirrorCulling.UpdateUsing(mainCulling, _mirrorFrustum, enabled: true);
        
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
        _gl.Viewport(0, 0, _target.Width, _target.Height);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        
        _skybox.Render(scene, reflView, proj);
        
        const float eps = 0.02f;
        var clip = new Vector4(0f, 1f, 0f, eps - h);

        // Mirroring flips triangle winding: front faces become back faces. Flip the winding
        // convention for the geometry pass so back-face culling keeps the correct (front) faces.
        _gl.FrontFace(FrontFaceDirection.CW);
        _main.Render(new RenderRequest(scene, deltaTime, GtaoTexture: 0, GtaoActive: false,
            _mirrorCulling, camera, reflView, reflPos, clip, CheapShading: true), ref stats);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _target.Framebuffer);
        _gl.BlitFramebuffer(0, 0, (int)_target.Width, (int)_target.Height,
            0, 0, (int)_target.Width, (int)_target.Height,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }
    
    private float ResolvePlaneHeight(Scene scene)
    {
        if (string.IsNullOrEmpty(_config.ReflectorEntity))
            return _config.PlaneHeight;

        foreach (var entity in scene.Entities)
            if (entity.Model is not null && entity.Name == _config.ReflectorEntity)
                return entity.GetWorldBounds().Max.Y;

        return _config.PlaneHeight;   // named entity not found this frame
    }
    
    private static bool PlaneInFrustum(Camera camera, float planeHeight)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        camera.GetFrustumCorners(corners);

        var minY = float.MaxValue;
        var maxY = float.MinValue;
        foreach (var c in corners)
        {
            if (c.Y < minY) minY = c.Y;
            if (c.Y > maxY) maxY = c.Y;
        }

        return planeHeight >= minY - VisibilityMargin && planeHeight <= maxY + VisibilityMargin;
    }
    
    private void AllocateMsaa()
    {
        uint w = _target.Width, h = _target.Height;

        _msaaFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

        _msaaColor = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColor);
        AllocateRenderbufferStorage(InternalFormat.Rgba16f, w, h);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _msaaColor);

        _msaaDepth = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepth);
        AllocateRenderbufferStorage(InternalFormat.DepthComponent24, w, h);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _msaaDepth);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    // RenderbufferStorageMultisample(..., 1, ...) is not a reliable way to request "no MSAA" —
    // on at least one real driver (Mesa llvmpipe) it silently allocates real multisampled
    // storage instead of honoring the requested sample count (see HDRFramebuffer, which has the
    // same pattern and hits the same issue). Only the plain, non-multisample entry point
    // guarantees genuinely single-sample storage.
    private void AllocateRenderbufferStorage(InternalFormat format, uint width, uint height)
    {
        if (_samples > 1)
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, _samples, format, width, height);
        else
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, format, width, height);
    }

    private void DestroyMsaa()
    {
        _gl.DeleteFramebuffer(_msaaFbo);
        _gl.DeleteRenderbuffer(_msaaColor);
        _gl.DeleteRenderbuffer(_msaaDepth);
    }

    public void Dispose()
    {
        _target.Dispose();
        DestroyMsaa();
    }
}
