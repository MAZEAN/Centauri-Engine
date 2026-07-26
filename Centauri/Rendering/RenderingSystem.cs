namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using System.Numerics;

using Config;
using Renderers;
using World;
using World.Components;
using Graphics.Geometry;
using Utils.Misc;
using UI;
using Loading;
using Editing.Undo;
using IBL;
using Postprocessing;
using Prepass;
using Shadows;
using DebugView;
using Profiling;
using GTAO;
using Culling;
using Reflections.Probes;
using Reflections.Planar;

internal sealed class RenderContext
{
    public FrameStats Stats;
    
    public bool GtaoActive;
    public bool SsrActive;
    public bool TaaActive;
    public bool PrepassRan;   // true when GeometryPrepass rendered a fresh, trustworthy depth this frame

    public CullingSystem Culling = null!;
    public GPUProfiler Profiler = null!;
}

public class RenderingSystem : IDisposable
{
    private const float Tolerance = 0.001f;
    
    private readonly GL            _gl;
    private readonly AppConfig     _config;
    
    private readonly MainRenderer  _mainRenderer;
    private readonly GridRenderer  _gridRenderer;
    private readonly DebugRenderer _debugRenderer;
    private readonly SkyboxRenderer _skyboxRenderer;
    private readonly ShadowMapper _shadows;
    private readonly SpotShadowMapper _spotShadows;
    private readonly IBLBaker _ibl;
    private readonly ReflectionProbeBaker _reflectionProbe;
    private readonly InstanceBuffer _instances;
    
    private UISystem _ui = null!;
    private PostProcessor _post = null!;
    private GeometryPrepass _prepass = null!;
    private GTAOPass _gtao = null!;
    private PlanarReflectionPass _planar = null!;
    private ZPrepass _zPrepass = null!;
    private CloudPass _clouds = null!;
    private BufferDebugView _bufferDebug = null!;
    
    private Vector2 _cloudInvViewport;

    private readonly RenderContext _context = new();

    private bool _probeBaked;
    private bool? _skyIsDay;

    private readonly FrameTimeTracker _frameTime = new();

    // The window's actual (native, unscaled) framebuffer size, cached so a mid-session
    // RenderConfig.RenderScale change (checked once per frame in Render()) can re-derive the
    // scaled render size and re-run Resize() without needing the IWindow itself, which is only
    // available transiently at InitializeComponents/OnResize time — see CheckRenderScale.
    private uint _outputWidth, _outputHeight;
    private float _lastRenderScale = 1f;
    
    public bool ImGuiWantsMouse    => _ui.WantsMouse;
    public bool ImGuiWantsKeyboard => _ui.WantsKeyboard;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl            = gl;
        _config        = config;
        
        _instances = new InstanceBuffer(gl);
        _context.Culling   = new CullingSystem(_config.Culling.CellSize, _config.Culling.OversizeFactor);
        
        _context.Profiler = new GPUProfiler(gl);
        _shadows = new ShadowMapper(gl, _config, _instances, _context.Profiler);
        _spotShadows = new SpotShadowMapper(gl, _config, _instances, _context.Profiler);
        _ibl = new IBLBaker(gl, _config.IBL);

        _mainRenderer   = new MainRenderer(gl, config, _ibl, _shadows, _spotShadows, _instances);
        _gridRenderer   = new GridRenderer(gl);
        _debugRenderer  = new DebugRenderer(gl, config);
        _skyboxRenderer = new SkyboxRenderer(gl, config);
        _reflectionProbe = new ReflectionProbeBaker(gl, _config.ReflectionProbe, _ibl, _mainRenderer, _skyboxRenderer);
    }
    
    public void InitializeComponents(IWindow window, IInputContext input, ResourceSystem resourceSystem, EntitySetLoader entitySetLoader, CommandHistory commandHistory)
    {
        var framebufferSize = window.FramebufferSize;
        _outputWidth  = (uint)framebufferSize.X;
        _outputHeight = (uint)framebufferSize.Y;
        _lastRenderScale = _config.Render.RenderScale;
        var (renderWidth, renderHeight) = ScaledSize(_outputWidth, _outputHeight);

        var hdr = new HDRFramebuffer(_gl, renderWidth, renderHeight, (uint)_config.Window.Samples);

        _post = new PostProcessor(_gl, hdr, _config, _context.Profiler,
            renderWidth, renderHeight, _outputWidth, _outputHeight);
        _ui   = new UISystem(_gl, _config, window, input, resourceSystem, entitySetLoader, commandHistory);
        _prepass = new GeometryPrepass(_gl, _config, renderWidth, renderHeight, _instances);
        _gtao    = new GTAOPass(_gl, _config.GTAO, renderWidth, renderHeight);
        _planar  = new PlanarReflectionPass(_gl, _config.PlanarReflection, _mainRenderer, _skyboxRenderer,
            renderWidth, renderHeight, (uint)_config.Window.Samples);
        _zPrepass = new ZPrepass(_gl, _config, _instances);
        _clouds  = new CloudPass(_gl, _config.Sky, _skyboxRenderer.Cube, renderWidth, renderHeight);
        _cloudInvViewport = new Vector2(1f / renderWidth, 1f / renderHeight);
        _bufferDebug = new BufferDebugView(_gl);
    }

    // AppConfig.Render.RenderScale times the window's real framebuffer size — see its own
    // comment for why only the scene (not the final tonemap output) scales. Clamped so a
    // pathological config value can't collapse a render target to 0 or upscale past native.
    private (uint width, uint height) ScaledSize(uint outputWidth, uint outputHeight)
    {
        var scale = Math.Clamp(_config.Render.RenderScale, 0.1f, 1f);
        return (
            Math.Max(1u, (uint)MathF.Round(outputWidth  * scale)),
            Math.Max(1u, (uint)MathF.Round(outputHeight * scale)));
    }

    // RenderScale is tunable at runtime via the UI, unlike the window size Resize() otherwise
    // reacts to — checked once a frame so a mid-session change re-derives the scaled render size
    // and re-runs the same resize path a real window resize would, without needing the IWindow
    // itself (only available transiently at InitializeComponents/Engine.OnResize time).
    private void CheckRenderScale()
    {
        if (Math.Abs(_config.Render.RenderScale - _lastRenderScale) < Tolerance) return;

        _lastRenderScale = _config.Render.RenderScale;
        Resize(_outputWidth, _outputHeight);
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

        _frameTime.Update(deltaTime);
        _context.Stats.FPS       = _frameTime.FPS;
        _context.Stats.FrameTime = _frameTime.FrameTime;
    }

    // Engine.OnUpdate calls this right after SimulationSystem.Update, so the Stats Overlay's
    // "Physics" section reflects the frame that was just simulated rather than lagging a frame
    // behind. FrameStats itself has no other producer outside RenderingSystem, hence the setter
    // rather than exposing _context.Stats for direct mutation.
    public void SetPhysicsStats(int dynamicBodies, int staticBodies, int steps, float stepMs)
    {
        _context.Stats.PhysicsDynamicBodies   = dynamicBodies;
        _context.Stats.PhysicsStaticBodies    = staticBodies;
        _context.Stats.PhysicsStepsThisFrame  = steps;
        _context.Stats.PhysicsStepMsThisFrame = stepMs;
    }

    public void Render(Scene scene, float deltaTime)
    {
        Tracy.Enabled = _config.Debug.TracyEnabled;
        using var frameZone = Tracy.Scope("Frame");

        CheckRenderScale();
        BeginFrame(scene);
        
        UpdateProceduralIbl(scene);
        
        var camera = scene.Cameras.Active;
        using (_context.Profiler.Measure("Clouds"))
            _clouds.Render(scene, camera.GetViewMatrix(), camera.GetProjectionMatrix());
        _skyboxRenderer.SetCloudMap(_clouds.Active ? _clouds.CloudTexture : 0, _cloudInvViewport);
        
        RenderPrePostComponents(scene, deltaTime);
        
        if (!_probeBaked || _config.ReflectionProbe.RebakeRequested)
        {
            _reflectionProbe.Bake(scene);
            _probeBaked = true;
            _config.ReflectionProbe.RebakeRequested = false;
            _config.ReflectionProbe.Baked = _reflectionProbe.Baked;
        }
        
        using (_context.Profiler.Measure("Planar"))
            _planar.Render(scene, deltaTime, _context.Culling, ref _context.Stats);
        
        _post.BeginScene();
        RenderCentralComponents(scene, deltaTime);

        // Composite() opens its own "SSR"/"Post" zones internally (GL_TIME_ELAPSED zones can't
        // nest, so this can't also be wrapped in an outer Measure() here — see PostProcessor).
        var compositeRequest = CreateCompositeRequest(scene, deltaTime);
        _post.Composite(in compositeRequest);
        
        RenderAfterPostComponents(scene, deltaTime);

        Tracy.FrameMark();
    }

    private CompositeRequest CreateCompositeRequest(Scene scene, float deltaTime)
    {
        var (iblInputs, planarInputs) = GetInputs(scene);
        
        var gBuffer = new GBufferTextures(_prepass.DepthTexture, _prepass.NormalTexture, _prepass.MaterialTexture);
        var compositeRequest = new CompositeRequest(
            Camera:       scene.Cameras.Active,
            GBuffer:      gBuffer,
            SsrAvailable: _context.SsrActive,
            TaaAvailable: _context.TaaActive,
            Ibl:          iblInputs,
            GtaoTexture:  _gtao.AoTexture,
            GtaoActive:   _context.GtaoActive,
            Planar:       planarInputs,
            DeltaTime:    deltaTime
        );
        
        return compositeRequest;
    }

    private (IblResolveInputs, PlanarResolveInputs) GetInputs(Scene scene)
    {
        var activeSky   = scene.Skyboxes.Active;
        var procedural  = _config.Sky.Procedural && _ibl.HasProceduralBake && DayNightCycle.IsDay(scene);
        var probeCfg    = _config.ReflectionProbe;
        var probeActive = probeCfg.Enabled && _reflectionProbe.Baked;
        var boxCenter   = new System.Numerics.Vector3(probeCfg.BoxCenter[0], probeCfg.BoxCenter[1], probeCfg.BoxCenter[2]);
        var boxHalf     = new System.Numerics.Vector3(probeCfg.BoxSize[0],   probeCfg.BoxSize[1],   probeCfg.BoxSize[2]);
        var iblInputs = new IblResolveInputs(
            PrefilterMap:     procedural ? _ibl.ProceduralPrefiltered : (activeSky?.PrefilteredMap ?? 0),
            BrdfLut:          _ibl.BrdfLut,
            MaxReflectionLod: _ibl.MaxReflectionLod,
            Intensity:        _config.IBL.IblIntensity,
            HasIbl:           procedural || activeSky is { IblBaked: true },
            ProbePrefilterMap:     probeActive ? _reflectionProbe.PrefilteredMap : 0,
            ProbeMaxReflectionLod: _reflectionProbe.MaxReflectionLod,
            ProbeIntensity:        probeCfg.Intensity,
            HasProbe:              probeActive,
            ProbePosition:         _reflectionProbe.Position,
            ProbeBoxMin:           boxCenter - boxHalf,
            ProbeBoxMax:           boxCenter + boxHalf,
            ProbeBoxFalloff:       MathF.Max(probeCfg.BoxFalloff, 1e-3f)
        );
        
        var planarCfg    = _config.PlanarReflection;
        var planarInputs = new PlanarResolveInputs(
            Map:        planarCfg.Enabled ? _planar.ReflectionTexture : 0,
            Has:        planarCfg.Enabled,
            Height:     _planar.PlaneHeight,
            Intensity:  _planar.Intensity,
            Distortion: _planar.Distortion,
            Blur:       _planar.Blur,
            MaxRoughness: _planar.MaxRoughness
        );
        return (iblInputs, planarInputs);
    }

    private void BeginFrame(Scene scene)
    {
        Time.BeginFrame();
        Graphics.Resources.GLTexture.SetAnisotropy(_gl, _config.Debug.AnisotropicFilter);
        scene.Lighting.Collect(scene.Entities);
        scene.Cameras.Active.JitterNdc = _post.NextTaaJitter();
        
        _context.Culling.Update(scene, scene.Cameras.Primary, _config.Debug.EnableCulling,
            _config.Culling.CellSize, _config.Culling.OversizeFactor);
        UpdateGridStats();
    }

    private void RenderCentralComponents(Scene scene, float deltaTime)
    {
        using (_context.Profiler.Measure("Sky+Grid"))
        {
            if (_config.Debug.ShowSkybox)
                _skyboxRenderer.Render(scene);

            if (_config.Debug.ShowGrid)
                _gridRenderer.Render(scene);
        }
        
        var zPrepassEnabled = _config.Debug.EnableZPrepass;

        // GeometryPrepass, when it ran this frame (GTAO/SSR/TAA active), already holds a full-
        // resolution depth buffer for this same camera/scene, with the same alpha-cutout as the
        // lit pass (see prepass.frag) — reusing it here saves ZPrepass's own full depth-only
        // redraw of the same geometry. Only valid when the HDR target isn't actually
        // multisampled (see HDRFramebuffer.TryBorrowDepth); otherwise (or when Prepass didn't
        // run this frame) falls back to ZPrepass's own draw exactly as before.
        var borrowedDepth = zPrepassEnabled && _context.PrepassRan && _post.TryBorrowPrepassDepth(_prepass.DepthTexture);

        if (zPrepassEnabled && !borrowedDepth)
            using (_context.Profiler.Measure("ZPrepass"))
                _zPrepass.Render(scene, scene.Cameras.Active, _context.Culling);

        // Forward reuses the depth ZPrepass (or the borrowed Prepass depth) just wrote instead
        // of its own fresh buffer: LEQUAL (not LESS) so the correctly-depth-matching visible
        // surface still passes, no writes since the depth is already right. Lets hardware
        // early-Z reject shading for anything already known to be hidden — the win scales with
        // overdraw, biggest exactly where it hurts most (dense, close-up, overlapping
        // alpha-tested foliage).
        // With EnableZPrepass off, Forward instead does a completely normal fresh depth
        // test/write (Less/true), exactly as it did before ZPrepass existed — a clean A/B
        // toggle for isolating bugs suspected to live in the depth-reuse mechanism itself.
        if (zPrepassEnabled)
        {
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.DepthMask(false);
        }
        
        using (_context.Profiler.Measure("Forward"))
            _mainRenderer.Render(scene, deltaTime, ref _context.Stats, _gtao.AoTexture, _context.GtaoActive, _context.Culling);
        
        if (zPrepassEnabled)
        {
            _gl.DepthFunc(DepthFunction.Less);
            _gl.DepthMask(true);
        }
        
        if (borrowedDepth)
            _post.ReleasePrepassDepth();

        using (_context.Profiler.Measure("Debug"))
        {
            var active = scene.Cameras.Active;
            _debugRenderer.Begin(active);

            _debugRenderer.DrawCameras(scene);
            _debugRenderer.DrawAllAABBs(scene, scene.Cameras.Primary.Frustum);
            _debugRenderer.DrawCullingGrid(scene, _context.Culling.Grid);
            _debugRenderer.DrawPhysicsColliders(scene);
            _debugRenderer.DrawSelection(scene);
            
            _debugRenderer.End();
        }
    }

    private void RenderPrePostComponents(Scene scene, float deltaTime)
    {
        UpdateDayNightSkybox(scene);

        _context.Profiler.BeginFrame(_config.Debug.ShowGPUTimings);

        // ShadowMapper.Render() opens its own "ShadowsSolid"/"ShadowsFoliage" zones internally
        // (GL_TIME_ELAPSED zones can't nest, so this can't also be wrapped in an outer Measure()
        // here — see ShadowMapper and PostProcessor.Composite for the same pattern).
        _shadows.Render(scene, _context.Culling, ref _context.Stats);
        _spotShadows.Render(scene, _context.Culling, scene.Cameras.Active, ref _context.Stats);

        // GTAO (and the Normals/Depth/AO debug views) all need the prepass buffers
        _context.GtaoActive = _config.GTAO.Enabled || _config.Debug.Shading == ShadingMode.AmbientOcclusion;
        _context.SsrActive  = _config.SSR.Enabled;
        _context.TaaActive  = _config.TAA.Enabled;
        var needPrepass = _context.GtaoActive || _context.SsrActive || _context.TaaActive ||
                           _config.Debug.Shading is not (ShadingMode.Shaded or ShadingMode.ParallaxDebug);
        
        if (needPrepass)
            using (_context.Profiler.Measure("Prepass"))
                _prepass.Render(scene, _context.Culling);
        
        _context.PrepassRan = needPrepass;

        if (_context.GtaoActive)
            using (_context.Profiler.Measure("GTAO"))
                _gtao.Render(_prepass.DepthTexture, _prepass.NormalTexture, scene.Cameras.Active);
    }


    private void RenderAfterPostComponents(Scene scene, float deltaTime)
    {
        _bufferDebug.Render(_config.Debug.Shading, _prepass.NormalTexture, _prepass.DepthTexture,
            _gtao.AoTexture, _post.VelocityTexture, _config.Camera.Near, _config.Camera.Far);
        
        _ui.Render(scene, in _context.Stats, _context.Profiler.Results);
    }
    
    private void UpdateProceduralIbl(Scene scene)
    {
        using var _ = Tracy.Scope("RenderingSystem.UpdateProceduralIbl");

        if (!_config.Sky.Procedural || scene.Lighting.DirectionalLights.Count == 0 || !DayNightCycle.IsDay(scene)) return;


        var sun    = scene.Lighting.DirectionalLights[0];
        var sunDir = -System.Numerics.Vector3.Normalize(sun.Direction);

        var cloudCoverage = _config.Sky.Clouds ? _config.Sky.CloudCoverage : 0f;
        _ibl.UpdateProcedural(sunDir, _config.Sky.Turbidity, _config.Sky.Intensity,
            cloudCoverage, _config.Sky.CloudScale, _config.Sky.CloudSpeed, _config.Sky.CloudShading);
    }
    
    private void UpdateDayNightSkybox(Scene scene)
    {
        if (scene.FindComponent<DayNightCycle>() is not { } cycle) return;

        var isDay = cycle.Daylight >= 0.5f;
        if (isDay == _skyIsDay) return;

        _skyIsDay = isDay;
        scene.Skyboxes.TrySetActive(isDay ? "Day" : "Night");
    }
    
    private void UpdateGridStats()
    {
        var grid = _context.Culling.Grid;
        _context.Stats.GridColumns  = grid.Columns;
        _context.Stats.GridRows     = grid.Rows;
        _context.Stats.GridOccupied = grid.OccupiedCells;
        _context.Stats.GridVisited  = grid.VisitedCells;
    }
    
    public void Resize(uint width, uint height)
    {
        _outputWidth  = width;
        _outputHeight = height;
        var (renderWidth, renderHeight) = ScaledSize(width, height);

        _post.Resize(renderWidth, renderHeight, _outputWidth, _outputHeight);
        _prepass.Resize(renderWidth, renderHeight);
        _gtao.Resize(renderWidth, renderHeight);
        // Not resized before this fix: PlanarReflectionPass's own target — and therefore its
        // reflection resolution/aspect — stayed pinned to whatever InitializeComponents saw at
        // startup, silently mismatched from the window after any real resize (RenderScale
        // exposed this by making window resize and render-scale changes share this same path).
        _planar.Resize(renderWidth, renderHeight);
        _clouds.Resize(renderWidth, renderHeight);
        _cloudInvViewport = new Vector2(1f / renderWidth, 1f / renderHeight);
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
        _gtao.Dispose();
        _clouds.Dispose();
        _zPrepass.Dispose();
        _bufferDebug.Dispose();
        _ibl.Dispose();
        _reflectionProbe.Dispose();
        _shadows.Dispose();
        _spotShadows.Dispose();
        _context.Profiler.Dispose();
        _instances.Dispose();
    }
}