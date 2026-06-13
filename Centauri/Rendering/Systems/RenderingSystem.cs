namespace Centauri.Rendering.Systems;

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

    private StatsOverlay _statsOverlay = null!;
    private ImGuiManager? _imGui;
    private InspectorPanel _inspector = null!;

    private FrameStats _stats;
    
    private float _fpsTimer;
    private int   _frameCount;
    
    public bool ImGuiWantsMouse    => _imGui?.WantsMouseCapture    ?? false;
    public bool ImGuiWantsKeyboard => _imGui?.WantsKeyboardCapture ?? false;

    public RenderingSystem(GL gl, AppConfig config)
    {
        _gl            = gl;
        _config        = config;
        _mainRenderer      = new MainRenderer(gl, config);
        _gridRenderer  = new GridRenderer(gl);
        _debugRenderer = new DebugRenderer(gl, config);
    }

    // called after GL and input are both ready
    public void InitializeImGui(IWindow window, IInputContext input)
    {
        _imGui = new ImGuiManager(_gl, _config.ImGui, window, input);
        _statsOverlay  = new StatsOverlay(_imGui.Font, _config);
        _inspector    = new InspectorPanel(_imGui.Font);
    }

    public void ToggleStatsOverlay() => _config.Debug.ToggleShowStatsOverlay();

    public void Update(float deltaTime)
    {
        _imGui?.Update(deltaTime);
        UpdateFPSCounter(deltaTime);
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

    public void Render(Scene scene, double deltaTime)
    {
        if (_config.Debug.ShowGrid)
            _gridRenderer.Render(scene);
        
        _mainRenderer.Render(scene, (float)deltaTime, ref _stats);

        if (_config.Debug.ShowDebugView || scene.Selected is not null)
        {
            var active = scene.GetActiveCamera();
            _debugRenderer.Begin(active);

            if (_config.Debug.ShowDebugView)
            {
                _debugRenderer.DrawCameras(scene);
                _debugRenderer.DrawAllAABBs(scene, scene.GetPrimaryCamera().Frustum);
            }

            _debugRenderer.DrawSelection(scene);
            _debugRenderer.End();
        }
        
        if (_config.Debug.ShowStatsOverlay)
            _statsOverlay.Render(scene, _stats);
        
        if (_config.Input.Mode == ViewMode.Edit)
            _inspector.Render(scene);
        
        _imGui?.Render();
    }

    public void Dispose()
    {
        _imGui?.Dispose();
        _gridRenderer.Dispose();
        _mainRenderer.Dispose();
        _debugRenderer.Dispose();
    }
}