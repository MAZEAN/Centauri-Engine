namespace Centauri.UI;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;

using Config;
using World;
using Utils.Misc;
using Panels.Inspector;
using Panels.Stats;
using Panels.Toolbar;
using Rendering;
using Rendering.Profiling;
using Loading;

// Owns the whole ImGui surface — the controller plus every panel.
// RenderingSystem holds one of these instead of juggling them individually.
public sealed class UISystem : IDisposable
{
    private readonly AppConfig      _config;
    private readonly ImGuiManager   _imGui;
    private readonly StatsOverlay   _statsOverlay;
    private readonly PropertiesPanel _properties;
    private readonly HierarchyPanel _outliner;
    private readonly ViewportToolbar _toolbar;
    
    public bool WantsMouse    => _imGui.WantsMouseCapture;
    public bool WantsKeyboard => _imGui.WantsKeyboardCapture;

    public UISystem(GL gl, AppConfig config, IWindow window, IInputContext input, ResourceSystem resourceSystem, EntitySetLoader entitySetLoader)
    {
        _config       = config;

        _imGui        = new ImGuiManager(gl, config.ImGui, window, input);

        _statsOverlay = new StatsOverlay(_imGui.Font, config);
        _properties    = new PropertiesPanel(_imGui.Font, _config, resourceSystem, entitySetLoader);
        _outliner = new HierarchyPanel(_imGui.Font, resourceSystem, entitySetLoader);
        _toolbar = new ViewportToolbar(_imGui.Font, config);
    }

    public void Update(float deltaTime) => _imGui.Update(deltaTime);

    public void Render(Scene scene, in FrameStats stats, IReadOnlyList<GpuTiming> gpuTimings)
    {
        using var _ = Tracy.Scope("UISystem.Render");

        if (_config.Debug.ShowStatsOverlay)
            using (Tracy.Scope("UISystem.Render.StatsOverlay"))
                _statsOverlay.Render(scene, stats, gpuTimings);

        using (Tracy.Scope("UISystem.Render.Toolbar"))
            _toolbar.Render();

        if (_config.Input.Mode == ViewMode.Edit)
        {
            using (Tracy.Scope("UISystem.Render.Outliner"))
                _outliner.Render(scene);
            using (Tracy.Scope("UISystem.Render.Properties"))
                _properties.Render(scene);
        }

        using (Tracy.Scope("UISystem.Render.ImGuiFlush"))
            _imGui.Render();
    }

    public void Dispose() => _imGui.Dispose();
}