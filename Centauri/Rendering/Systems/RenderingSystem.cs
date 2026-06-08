namespace Centauri.Rendering.Systems;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Config;
using Renderers;
using World;
using Utils.Misc;

public class RenderingSystem : IDisposable
{
    private readonly GL            _gl;
    private readonly AppConfig     _config;
    private readonly Renderer      _renderer;
    private readonly GridRenderer  _gridRenderer;
    private readonly DebugRenderer _debugRenderer;
    private readonly StatsOverlay  _statsOverlay;
    private          ImGuiSystem?  _imGui;

    private FrameStats _stats;

    // FPS smoothing
    private float _fpsTimer;
    private int   _frameCount;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl            = gl;
        _config        = config;
        _renderer      = new Renderer(gl, config);
        _gridRenderer  = new GridRenderer(gl, config.Window);
        _debugRenderer = new DebugRenderer(gl);
        _statsOverlay  = new StatsOverlay();
    }

    // called after GL and input are both ready
    public void InitializeImGui(IWindow window, IInputContext input)
    {
        _imGui = new ImGuiSystem(_gl, window, input);
    }

    public void ToggleStatsOverlay() => _statsOverlay.Toggle();

    public void Update(float deltaTime)
    {
        _imGui?.Update(deltaTime);
        UpdateFPSCounter(deltaTime);
    }

    private void UpdateFPSCounter(float deltaTime)
    {
        // FPS + frametime smoothed over 1 second
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

    public void Render(Scene scene, double deltaTime)
    {
        if (scene.DebugSettings.ShowGrid)
            _gridRenderer.Render(scene);
        
        _renderer.Render(scene, (float)deltaTime, ref _stats);

        if (scene.DebugSettings.ShowDebugView)
        {
            var active = scene.GetActiveCamera();
            var cullingCamera = scene.GetPrimaryCamera();

            _debugRenderer.Begin(active);
            _debugRenderer.DrawCameras(scene);
            _debugRenderer.DrawAllAABBs(scene, cullingCamera.Frustum);
            _debugRenderer.End();
        }
        
        if (_statsOverlay.IsVisible)
            _statsOverlay.Render(_stats);
        
        _imGui?.Render();
    }

    public void Dispose()
    {
        _imGui?.Dispose();
        _gridRenderer.Dispose();
        _debugRenderer.Dispose();
    }
}