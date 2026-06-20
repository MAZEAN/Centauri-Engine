namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;

using Config;
using Renderers;
using World;
using Utils.Misc;
using UI;
using IBL;
using Postprocessing;
using Shadows;

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
    
    private UISystem _ui = null!;
    private PostProcessor _post = null!;

    private FrameStats _stats;
    
    private float _fpsTimer;
    private int   _frameCount;
    
    public bool ImGuiWantsMouse    => _ui.WantsMouse;
    public bool ImGuiWantsKeyboard => _ui.WantsKeyboard;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl            = gl;
        _config        = config;
        
        _ibl = new IBLBaker(gl, _config.IBLConfig);  
        _shadows = new ShadowMapper(gl, _config);
        
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
        
        _post = new PostProcessor(_gl, hdr, _config.ColorGrading);
        _ui   = new UISystem(_gl, _config, window, input, _config.ColorGrading);   // shares the same instance
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

    public void Render(Scene scene, double deltaTime)
    {
        scene.Lighting.Collect(scene.Entities);
        
        _shadows.Render(scene);
        
        _post.BeginScene();

        if (_config.Debug.ShowSkybox) 
            _skyboxRenderer.Render(scene);
        if (_config.Debug.ShowGrid)   
            _gridRenderer.Render(scene);
        
        _mainRenderer.Render(scene, (float)deltaTime, ref _stats);

        if (_config.Debug.ShowDebugView || scene.Selected is not null)
        {
            var active = scene.Cameras.Active;
            _debugRenderer.Begin(active);
            
            if (_config.Debug.ShowDebugView)
            {
                _debugRenderer.DrawCameras(scene);
                _debugRenderer.DrawAllAABBs(scene, scene.Cameras.Primary.Frustum);
            }
            
            _debugRenderer.DrawSelection(scene);
            _debugRenderer.End();
        }

        _post.Composite();              // resolve + tonemap to backbuffer
        _ui.Render(scene, in _stats);   // UI on top, ungraded
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
    
    public void Resize(uint width, uint height) => _post.Resize(width, height);

    public void Dispose()
    {
        _gridRenderer.Dispose();
        _mainRenderer.Dispose();
        _debugRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _ui.Dispose();
        _post.Dispose();
        _ibl.Dispose();
        _shadows.Dispose();
    }
}