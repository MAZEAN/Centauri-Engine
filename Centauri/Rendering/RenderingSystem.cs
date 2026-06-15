namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;

using Config;
using Renderers;
using World;
using Utils.Misc;
using UI;

public class RenderingSystem : IDisposable
{
    private readonly GL            _gl;
    private readonly AppConfig     _config;
    private readonly MainRenderer  _mainRenderer;
    private readonly GridRenderer  _gridRenderer;
    private readonly DebugRenderer _debugRenderer;
    private readonly SkyboxRenderer _skyboxRenderer;

    private UISystem _ui;

    private FrameStats _stats;
    
    private float _fpsTimer;
    private int   _frameCount;
    
    public bool ImGuiWantsMouse    => _ui.WantsMouse;
    public bool ImGuiWantsKeyboard => _ui.WantsKeyboard;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl            = gl;
        _config        = config;
        
        _mainRenderer   = new MainRenderer(gl, config);
        _gridRenderer   = new GridRenderer(gl);
        _debugRenderer  = new DebugRenderer(gl, config);
        _skyboxRenderer = new SkyboxRenderer(gl);
    }

    // called after GL and input are both ready
    public void InitializeUI(IWindow window, IInputContext input)
    {
        _ui = new UISystem(_gl, _config, window, input);
    }

    public void Update(float deltaTime)
    {
        _ui.Update(deltaTime);
        UpdateFPSCounter(deltaTime);
    }

    public void Render(Scene scene, double deltaTime)
    {
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
        
        _ui.Render(scene, in _stats);
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

    public void Dispose()
    {
        _ui.Dispose();
        _gridRenderer.Dispose();
        _mainRenderer.Dispose();
        _debugRenderer.Dispose();
        _skyboxRenderer.Dispose();
    }
}