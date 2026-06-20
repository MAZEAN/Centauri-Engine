namespace Centauri.UI;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;

using Config;
using World;
using Utils.Misc;
using Panels;

// Owns the whole ImGui surface — the controller plus every panel.
// RenderingSystem holds one of these instead of juggling them individually.
public sealed class UISystem : IDisposable
{
    private readonly AppConfig      _config;
    private readonly ImGuiManager   _imGui;
    private readonly StatsOverlay   _statsOverlay;
    private readonly PropertiesPanel _properties;
    private readonly OutlinerPanel _outliner;

    public UISystem(GL gl, AppConfig config, IWindow window, IInputContext input, ColorGrading grading)
    {
        _config       = config;
        _imGui        = new ImGuiManager(gl, config.ImGui, window, input);
        _statsOverlay = new StatsOverlay(_imGui.Font, config);
        _properties    = new PropertiesPanel(_imGui.Font, _config, grading);
        _outliner = new OutlinerPanel(_imGui.Font);
    }

    public bool WantsMouse    => _imGui.WantsMouseCapture;
    public bool WantsKeyboard => _imGui.WantsKeyboardCapture;

    public void Update(float deltaTime) => _imGui.Update(deltaTime);

    public void Render(Scene scene, in FrameStats stats)
    {
        if (_config.Debug.ShowStatsOverlay)
            _statsOverlay.Render(scene, stats);

        if (_config.Input.Mode == ViewMode.Edit)
        {
            _outliner.Render(scene);
            _properties.Render(scene);
        }

        _imGui.Render();   // ImGui draw pass — always last
    }

    public void Dispose() => _imGui.Dispose();
}