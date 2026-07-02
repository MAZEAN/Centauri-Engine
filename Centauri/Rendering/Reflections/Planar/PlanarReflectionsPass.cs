namespace Centauri.Rendering.Reflections.Planar;

using Silk.NET.OpenGL;
using System.Numerics;

using Config;
using World;
using Renderers;
using Culling;
using Targets;
using Utils.Misc;

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
    private readonly GL _gl;
    private readonly PlanarReflectionConfig _config;
    private readonly MainRenderer _main;
    private readonly SkyboxRenderer _skybox;
    private readonly CullingSystem _noCulling = new();   // never enabled -> everything is visible in the mirror

    private readonly RenderTarget _target;

    private float _resolvedHeight;

    public uint  ReflectionTexture => _target.ColorTextures[0];
    public bool  Enabled     => _config.Enabled;
    public float PlaneHeight => _resolvedHeight;   // resolved in Render(): reflector entity's top if bound, else config
    public float Intensity   => _config.Intensity;
    public float Distortion  => _config.Distortion;

    public PlanarReflectionPass(GL gl, PlanarReflectionConfig config, MainRenderer main,
        SkyboxRenderer skybox, uint width, uint height)
    {
        _gl = gl;
        _config = config;
        _main = main;
        _skybox = skybox;

        var div = _config.HalfResolution ? 2u : 1u;
        _target = new RenderTarget(gl, Math.Max(1u, width / div), Math.Max(1u, height / div),
            [InternalFormat.Rgba16f], withDepth: true, filter: GLEnum.Linear);
    }

    public void Resize(uint width, uint height)
    {
        var div = _config.HalfResolution ? 2u : 1u;
        _target.Resize(Math.Max(1u, width / div), Math.Max(1u, height / div));
    }

    public void Render(Scene scene, float deltaTime, ref FrameStats stats)
    {
        if (!_config.Enabled) return;

        var camera = scene.Cameras.Active;
        var h = ResolvePlaneHeight(scene);
        _resolvedHeight = h;

        // Reflect world space across the horizontal plane y = h, then view with the main
        // camera: (x, y, z) -> (x, 2h - y, z). System.Numerics uses row vectors (v * M), so
        // the reflected view is (reflect * mainView) and reflected geometry is what the mirror
        // camera sees.
        var reflect = new Matrix4x4(
            1f,    0f, 0f, 0f,
            0f,   -1f, 0f, 0f,
            0f,    0f, 1f, 0f,
            0f, 2f * h, 0f, 1f);

        var reflView = reflect * camera.GetViewMatrix();
        var reflPos  = new Vector3(camera.Position.X, 2f * h - camera.Position.Y, camera.Position.Z);
        var proj     = camera.GetProjectionMatrix();

        // no culling: the main frustum's visibility set is wrong for the mirrored view, so draw
        // everything (bounded cheaply by the half-res target).
        _noCulling.Update(scene, camera, enabled: false, cellSize: 16f, oversizeFactor: 8f);

        _target.Bind();
        _target.Clear(0f, 0f, 0f, 1f);
        _gl.Enable(EnableCap.DepthTest);

        // sky first — fills the reflection wherever no geometry is hit (SkyboxRenderer disables
        // face culling itself, so the mirrored winding is a non-issue here)
        _skybox.Render(scene, reflView, proj);
        
        const float eps = 0.02f;
        var clip = new Vector4(0f, 1f, 0f, eps - h);

        // Mirroring flips triangle winding: front faces become back faces. Flip the winding
        // convention for the geometry pass so back-face culling keeps the correct (front) faces.
        _gl.FrontFace(FrontFaceDirection.CW);
        _main.Render(scene, deltaTime, ref stats, ssaoTexture: 0, ssaoActive: false,
            _noCulling, camera, reflView, reflPos, clip);
        _gl.FrontFace(FrontFaceDirection.Ccw);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    // The reflector surface height. If ReflectorEntity names a scene entity, track the top of
    // its world AABB (so the plane auto-matches the floor and follows it if it moves); otherwise
    // fall back to the authored PlaneHeight. This also keeps the resolve's height mask exact.
    private float ResolvePlaneHeight(Scene scene)
    {
        if (string.IsNullOrEmpty(_config.ReflectorEntity))
            return _config.PlaneHeight;

        foreach (var entity in scene.Entities)
            if (entity.Model is not null && entity.Name == _config.ReflectorEntity)
                return entity.GetWorldBounds().Max.Y;

        return _config.PlaneHeight;   // named entity not found this frame
    }

    public void Dispose() => _target.Dispose();
}
