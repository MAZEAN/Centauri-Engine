namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;

using Config;
using Renderers;
using World;
using World.Components;
using Utils.Misc;
using UI;
using IBL;
using Postprocessing;
using Prepass;
using Shadows;
using DebugView;
using Profiling;
using SSAO;

public class RenderingSystem : IDisposable
{
    private readonly GL            _gl;
    private readonly AppConfig     _config;
    private readonly MainRenderer  _mainRenderer;
    private readonly GridRenderer  _gridRenderer;
    private readonly DebugRenderer _debugRenderer;
    private readonly SkyboxRenderer _skyboxRenderer;
    private readonly ShadowMapper _shadows;
    private readonly IBLBaker _ibl;
    private readonly GpuProfiler _profiler;
    
    private UISystem _ui = null!;
    private PostProcessor _post = null!;
    private GeometryPrepass _prepass = null!;
    private SsaoPass _ssao = null!;
    private BufferDebugView _bufferDebug = null!;

    private FrameStats _stats;
    
    // Flags
    private bool _ssaoActive;   // SSAO ran this frame → bind/apply its result
    private bool? _skyIsDay;
    private bool _ssrActive;
    
    private float _fpsTimer;
    private int   _frameCount;
    
    public bool ImGuiWantsMouse    => _ui.WantsMouse;
    public bool ImGuiWantsKeyboard => _ui.WantsKeyboard;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl            = gl;
        _config        = config;
        
        _shadows = new ShadowMapper(gl, _config);
        _ibl = new IBLBaker(gl, _config.IBLConfig);
        _profiler = new GpuProfiler(gl);
        
        _mainRenderer   = new MainRenderer(gl, config, _ibl, _shadows);
        _gridRenderer   = new GridRenderer(gl);
        _debugRenderer  = new DebugRenderer(gl, config);
        _skyboxRenderer = new SkyboxRenderer(gl);
    }
    
    public void InitializeComponents(IWindow window, IInputContext input)
    {
        var framebufferSize = window.FramebufferSize;
        var hdr = new HDRFramebuffer(
            _gl, (uint)framebufferSize.X, (uint)framebufferSize.Y, (uint)_config.Window.Samples
        );
        
        _post = new PostProcessor(_gl, hdr, _config.ColorGrading, _config.Bloom, _config.SSR,
            (uint)framebufferSize.X, (uint)framebufferSize.Y);
        _ui   = new UISystem(_gl, _config, window, input, _config.ColorGrading);
        _prepass = new GeometryPrepass(_gl, _config, (uint)framebufferSize.X, (uint)framebufferSize.Y);
        _ssao    = new SsaoPass(_gl, _config.SSAO, (uint)framebufferSize.X, (uint)framebufferSize.Y);
        _bufferDebug = new BufferDebugView(_gl);
    }
    
    public void BakeEnvironments(Scene scene)
    {
        foreach (var sky in scene.Skyboxes.All)
        {
            if (!sky.IblBaked)
                (sky.IrradianceMap, sky.PrefilteredMap) = _ibl.Bake(sky.Texture, sky.Exposure);
        }
    }

    public void Update(float deltaTime)
    {
        _ui.Update(deltaTime);
        UpdateFPSCounter(deltaTime);
    }

    public void Render(Scene scene, float deltaTime)
    {
        scene.Lighting.Collect(scene.Entities);
        
        RenderPrePostComponents(scene, deltaTime);
        
        _post.BeginScene();
        RenderCentralComponents(scene, deltaTime);
        
        using (_profiler.Measure("Post"))
            _post.Composite(scene.Cameras.Active, _prepass.DepthTexture, _prepass.NormalTexture,
                _prepass.MaterialTexture, _ssrActive);
        
        RenderAfterPostComponents(scene, deltaTime);
    }

    private void RenderCentralComponents(Scene scene, float deltaTime)
    {
        using (_profiler.Measure("Sky+Grid"))
        {
            if (_config.Debug.ShowSkybox)
                _skyboxRenderer.Render(scene);

            if (_config.Debug.ShowGrid)
                _gridRenderer.Render(scene);
        }

        using (_profiler.Measure("Forward"))
            _mainRenderer.Render(scene, deltaTime, ref _stats, _ssao.AoTexture, _ssaoActive);

        using (_profiler.Measure("Debug"))
        {
            var active = scene.Cameras.Active;
            _debugRenderer.Begin(active);

            _debugRenderer.DrawCameras(scene);
            _debugRenderer.DrawAllAABBs(scene, scene.Cameras.Primary.Frustum);

            _debugRenderer.DrawSelection(scene);
            _debugRenderer.End();
        }
    }

    private void RenderPrePostComponents(Scene scene, float deltaTime)
    {
        UpdateDayNightSkybox(scene);

        _profiler.BeginFrame(_config.Debug.ShowGPUTimings);

        using (_profiler.Measure("Shadows"))
            _shadows.Render(scene, ref _stats);

        // SSAO (and the Normals/Depth/AO debug views) all need the prepass buffers
        _ssaoActive = _config.SSAO.Enabled || _config.Debug.Shading == ShadingMode.AmbientOcclusion;
        _ssrActive  = _config.SSR.Enabled;
        var needPrepass = _ssaoActive || _ssrActive || _config.Debug.Shading != ShadingMode.Shaded;

        if (needPrepass)
            using (_profiler.Measure("Prepass"))
                _prepass.Render(scene);

        if (_ssaoActive)
            using (_profiler.Measure("SSAO"))
                _ssao.Render(_prepass.DepthTexture, _prepass.NormalTexture, scene.Cameras.Active);
    }


    private void RenderAfterPostComponents(Scene scene, float deltaTime)
    {
        _bufferDebug.Render(_config.Debug.Shading, _prepass.NormalTexture, _prepass.DepthTexture,
            _ssao.AoTexture, _config.Camera.Near, _config.Camera.Far);
        
        _ui.Render(scene, in _stats, _profiler.Results);
    }
    
    private void UpdateDayNightSkybox(Scene scene)
    {
        if (scene.FindComponent<DayNightCycle>() is not { } cycle) return;

        var isDay = cycle.Daylight >= 0.5f;
        if (isDay == _skyIsDay) return;

        _skyIsDay = isDay;
        scene.Skyboxes.TrySetActive(isDay ? "Day" : "Night");
    }
    
    private void UpdateFPSCounter(float deltaTime)
    {
        // FPS + frame time smoothed over 1 second
        _fpsTimer   += deltaTime;
        _frameCount += 1;

        if (_fpsTimer >= 1.0f)
        {
            _stats.FPS       = _frameCount / _fpsTimer;
            _stats.FrameTime = 1000f / _stats.FPS;
            _frameCount      = 0;
            _fpsTimer        = 0f;
        }
    }
    
    public void Resize(uint width, uint height)
    {
        _post.Resize(width, height);
        _prepass.Resize(width, height);
        _ssao.Resize(width, height);
    }

    public void Dispose()
    {
        _gridRenderer.Dispose();
        _mainRenderer.Dispose();
        _debugRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _ui.Dispose();
        _post.Dispose();
        _prepass.Dispose();
        _ssao.Dispose();
        _bufferDebug.Dispose();
        _ibl.Dispose();
        _shadows.Dispose();
        _profiler.Dispose();
    }
}