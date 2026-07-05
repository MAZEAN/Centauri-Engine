namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;

using Config;
using Renderers;
using World;
using World.Components;
using Graphics.Geometry;
using Utils.Misc;
using UI;
using IBL;
using Postprocessing;
using Prepass;
using Shadows;
using DebugView;
using Profiling;
using SSAO;
using Culling;
using Reflections.Probes;
using Reflections.Planar;

internal sealed class RenderContext
{
    public FrameStats Stats;
    
    public bool SsaoActive;
    public bool SsrActive;
    public bool TaaActive;

    public CullingSystem Culling = null!;
    public GPUProfiler Profiler = null!;
}

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
    private readonly ReflectionProbeBaker _reflectionProbe;
    private readonly InstanceBuffer _instances;
    
    private UISystem _ui = null!;
    private PostProcessor _post = null!;
    private GeometryPrepass _prepass = null!;
    private SsaoPass _ssao = null!;
    private PlanarReflectionPass _planar = null!;
    private BufferDebugView _bufferDebug = null!;
    
    private readonly RenderContext _context = new();
    
    private bool _probeBaked;
    private bool? _skyIsDay;

    private readonly FrameTimeTracker _frameTime = new();
    
    public bool ImGuiWantsMouse    => _ui.WantsMouse;
    public bool ImGuiWantsKeyboard => _ui.WantsKeyboard;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl            = gl;
        _config        = config;
        
        _instances = new InstanceBuffer(gl);
        _context.Culling   = new CullingSystem(_config.Culling.CellSize, _config.Culling.OversizeFactor);
        
        _shadows = new ShadowMapper(gl, _config, _instances);
        _ibl = new IBLBaker(gl, _config.IBLConfig);
        _context.Profiler = new GPUProfiler(gl);
        
        _mainRenderer   = new MainRenderer(gl, config, _ibl, _shadows, _instances);
        _gridRenderer   = new GridRenderer(gl);
        _debugRenderer  = new DebugRenderer(gl, config);
        _skyboxRenderer = new SkyboxRenderer(gl, config);
        _reflectionProbe = new ReflectionProbeBaker(gl, _config.ReflectionProbe, _ibl, _mainRenderer, _skyboxRenderer);
    }
    
    public void InitializeComponents(IWindow window, IInputContext input)
    {
        var framebufferSize = window.FramebufferSize;
        var hdr = new HDRFramebuffer(
            _gl, (uint)framebufferSize.X, (uint)framebufferSize.Y, (uint)_config.Window.Samples
        );
        
        _post = new PostProcessor(_gl, hdr, _config, (uint)framebufferSize.X, (uint)framebufferSize.Y);
        _ui   = new UISystem(_gl, _config, window, input);
        _prepass = new GeometryPrepass(_gl, _config, (uint)framebufferSize.X, (uint)framebufferSize.Y, _instances);
        _ssao    = new SsaoPass(_gl, _config.SSAO, (uint)framebufferSize.X, (uint)framebufferSize.Y);
        _planar  = new PlanarReflectionPass(_gl, _config.PlanarReflection, _mainRenderer, _skyboxRenderer,
            (uint)framebufferSize.X, (uint)framebufferSize.Y, (uint)_config.Window.Samples);
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

        _frameTime.Update(deltaTime);
        _context.Stats.FPS       = _frameTime.FPS;
        _context.Stats.FrameTime = _frameTime.FrameTime;
    }

    public void Render(Scene scene, float deltaTime)
    {
        BeginFrame(scene);
        
        UpdateProceduralIbl(scene);
        
        RenderPrePostComponents(scene, deltaTime);
        
        if (!_probeBaked || _config.ReflectionProbe.RebakeRequested)
        {
            _reflectionProbe.Bake(scene);
            _probeBaked = true;
            _config.ReflectionProbe.RebakeRequested = false;
            _config.ReflectionProbe.Baked = _reflectionProbe.Baked;
        }
        
        using (_context.Profiler.Measure("Planar"))
            _planar.Render(scene, deltaTime, ref _context.Stats);
        
        _post.BeginScene();
        RenderCentralComponents(scene, deltaTime);
        
        var (iblInputs, planarInputs) = GetInputs(scene);
        
        var gBuffer = new GBufferTextures(_prepass.DepthTexture, _prepass.NormalTexture, _prepass.MaterialTexture);
        using (_context.Profiler.Measure("Post"))
            _post.Composite(scene.Cameras.Active, in gBuffer, _context.SsrActive, _context.TaaActive, in iblInputs,
                _ssao.AoTexture, _context.SsaoActive, in planarInputs);
        
        RenderAfterPostComponents(scene, deltaTime);
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
            Intensity:        _config.IBLConfig.IblIntensity,
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
            Blur:       _planar.Blur
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

        using (_context.Profiler.Measure("Forward"))
            _mainRenderer.Render(scene, deltaTime, ref _context.Stats, _ssao.AoTexture, _context.SsaoActive, _context.Culling);

        using (_context.Profiler.Measure("Debug"))
        {
            var active = scene.Cameras.Active;
            _debugRenderer.Begin(active);

            _debugRenderer.DrawCameras(scene);
            _debugRenderer.DrawAllAABBs(scene, scene.Cameras.Primary.Frustum);
            _debugRenderer.DrawCullingGrid(scene, _context.Culling.Grid);
            _debugRenderer.DrawSelection(scene);
            
            _debugRenderer.End();
        }
    }

    private void RenderPrePostComponents(Scene scene, float deltaTime)
    {
        UpdateDayNightSkybox(scene);

        _context.Profiler.BeginFrame(_config.Debug.ShowGPUTimings);

        using (_context.Profiler.Measure("Shadows"))
            _shadows.Render(scene, _context.Culling, ref _context.Stats);

        // SSAO (and the Normals/Depth/AO debug views) all need the prepass buffers
        _context.SsaoActive = _config.SSAO.Enabled || _config.Debug.Shading == ShadingMode.AmbientOcclusion;
        _context.SsrActive  = _config.SSR.Enabled;
        _context.TaaActive  = _config.TAA.Enabled;
        var needPrepass = _context.SsaoActive || _context.SsrActive || _context.TaaActive || _config.Debug.Shading != ShadingMode.Shaded;
        
        if (needPrepass)
            using (_context.Profiler.Measure("Prepass"))
                _prepass.Render(scene, _context.Culling);

        if (_context.SsaoActive)
            using (_context.Profiler.Measure("SSAO"))
                _ssao.Render(_prepass.DepthTexture, _prepass.NormalTexture, scene.Cameras.Active);
    }


    private void RenderAfterPostComponents(Scene scene, float deltaTime)
    {
        _bufferDebug.Render(_config.Debug.Shading, _prepass.NormalTexture, _prepass.DepthTexture,
            _ssao.AoTexture, _post.VelocityTexture, _config.Camera.Near, _config.Camera.Far);
        
        _ui.Render(scene, in _context.Stats, _context.Profiler.Results);
    }
    
    private void UpdateProceduralIbl(Scene scene)
    {
        if (!_config.Sky.Procedural || scene.Lighting.DirectionalLights.Count == 0 || !DayNightCycle.IsDay(scene)) return;


        var sun    = scene.Lighting.DirectionalLights[0];
        var sunDir = -System.Numerics.Vector3.Normalize(sun.Direction);

        var cloudCoverage = _config.Sky.Clouds ? _config.Sky.CloudCoverage : 0f;
        _ibl.UpdateProcedural(sunDir, _config.Sky.Turbidity, _config.Sky.Intensity,
            cloudCoverage, _config.Sky.CloudScale, _config.Sky.CloudSpeed);
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
        _reflectionProbe.Dispose();
        _shadows.Dispose();
        _context.Profiler.Dispose();
        _instances.Dispose();
    }
}