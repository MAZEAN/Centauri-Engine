namespace Centauri.Rendering.Probes;

using Silk.NET.OpenGL;
using Silk.NET.Maths;
using System.Numerics;

using Centauri.Config;
using Centauri.World;
using Centauri.Rendering.IBL;
using Centauri.Rendering.Culling;
using Centauri.Utils.Misc;

// Captures a single, static, global reflection probe: renders the real scene from a fixed
// world position into a cubemap (6 faces, no per-face culling since this is a one-time bake,
// not a per-frame cost) and hands it to IBLBaker's existing GGX prefilter — the same math the
// skybox's own IBL bake uses. Unlike the skybox, no diffuse/irradiance term is produced; this
// probe only feeds SSR's resolve fallback (Assets/Shaders/SSR/ssr_resolve.frag), which needs
// the prefiltered specular map and BRDF LUT only, not a diffuse ambient term.
//
// Camera can't represent 2 of the 6 cubemap faces (its yaw/pitch parameterization degenerates
// when looking straight up/down), so this renders via MainRenderer's explicit view-matrix
// overload instead of driving a Camera through the scene's normal orbit/look controls.
//
// Known limitations of this v1 scope: baked once (not re-bakeable at runtime), single global
// probe (no placement/volumes/blending), and captured using whatever shadow-cascade data the
// main camera produced this frame — cascades are fit to the main view, not the probe, so
// shadows in directions the main camera isn't currently facing may be stale or absent.
public sealed class ReflectionProbeBaker : IDisposable
{
    private readonly GL _gl;
    private readonly ReflectionProbeConfig _config;
    private readonly IBLBaker _ibl;
    private readonly MainRenderer _mainRenderer;

    private readonly uint _fbo, _rbo;
    private readonly CullingSystem _noCulling = new();   // never enabled -> IsVisible() always true
    private readonly Camera _probeCamera;

    public Vector3 Position { get; }
    public uint PrefilteredMap { get; private set; }
    public int  MaxReflectionLod => _ibl.MaxReflectionLod;
    public bool Baked => PrefilteredMap != 0;

    public ReflectionProbeBaker(GL gl, ReflectionProbeConfig config, IBLBaker ibl, MainRenderer mainRenderer)
    {
        _gl = gl;
        _config = config;
        _ibl = ibl;
        _mainRenderer = mainRenderer;

        Position = new Vector3(config.Position[0], config.Position[1], config.Position[2]);

        _fbo = gl.GenFramebuffer();
        _rbo = gl.GenRenderbuffer();

        // Only used for its projection matrix (90 deg FOV, 1:1 aspect — the standard cubemap
        // face frustum); its own GetViewMatrix() is never called, so the yaw/pitch it was
        // constructed with (and the pole singularity that comes with them) never matters.
        _probeCamera = new Camera(
            new CameraConfig { FOV = 90f, Near = 0.1f, Far = 1000f },
            "ReflectionProbe", Position, Vector3.UnitY, yaw: 0f, pitch: 0f);
        _probeCamera.SetAspectRatio(new Vector2D<int>(1, 1));
    }

    // Call once the scene's shadow maps are valid for the frame (i.e. after
    // ShadowMapper.Render), so captured surfaces are lit/shadowed like the main view.
    public void Bake(Scene scene)
    {
        if (!_config.Enabled) return;

        var env = _ibl.CreateCubemap(_config.Resolution, mips: true);

        // culling is never enabled on this instance, so IsVisible() is always true and the
        // camera argument below is unused — every entity draws into every face.
        _noCulling.Update(scene, scene.Cameras.Active, enabled: false, cellSize: 16f, oversizeFactor: 8f);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _rbo);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
            _config.Resolution, _config.Resolution);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _rbo);
        _gl.Viewport(0, 0, _config.Resolution, _config.Resolution);
        _gl.Enable(EnableCap.DepthTest);

        var views = FaceViews(Position);

        for (var i = 0; i < 6; i++)
        {
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + i), env, 0);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var stats = new FrameStats();
            _mainRenderer.Render(scene, 0f, ref stats, ssaoTexture: 0, ssaoActive: false, _noCulling,
                _probeCamera, views[i], Position);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.TextureCubeMap, env);
        _gl.GenerateMipmap(TextureTarget.TextureCubeMap);

        PrefilteredMap = _ibl.PrefilterEnvironment(env, _config.Resolution);

        _gl.DeleteTexture(env);
    }

    // Standard OpenGL cubemap face directions/up-vectors, translated to the probe's world
    // position — matches IBLBaker.CaptureViews()'s convention so the resulting cubemap's face
    // orientation is consistent with how textureLod(uPrefilterMap, R, ...) expects to sample it.
    private static Matrix4x4[] FaceViews(Vector3 origin) =>
    [
        Matrix4x4.CreateLookAt(origin, origin + new Vector3( 1, 0, 0), new(0,-1, 0)),
        Matrix4x4.CreateLookAt(origin, origin + new Vector3(-1, 0, 0), new(0,-1, 0)),
        Matrix4x4.CreateLookAt(origin, origin + new Vector3( 0, 1, 0), new(0, 0, 1)),
        Matrix4x4.CreateLookAt(origin, origin + new Vector3( 0,-1, 0), new(0, 0,-1)),
        Matrix4x4.CreateLookAt(origin, origin + new Vector3( 0, 0, 1), new(0,-1, 0)),
        Matrix4x4.CreateLookAt(origin, origin + new Vector3( 0, 0,-1), new(0,-1, 0)),
    ];

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteRenderbuffer(_rbo);
        if (PrefilteredMap != 0)
            _gl.DeleteTexture(PrefilteredMap);
    }
}
