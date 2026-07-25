namespace Centauri.UI;

using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImGuiNET;

using Config;
using World;
using Utils.Misc;
using Panels.Inspector;
using Panels.Stats;
using Panels.Toolbar;
using Rendering;
using Rendering.Profiling;
using Loading;
using Gizmos;
using Layout;
using Common;

// Owns the whole ImGui surface — the controller plus every panel. RenderingSystem holds one of
// these instead of juggling them individually.
//
// Layout is fully docked (see EditorLayout.cs): TopBar decides which EditorWorkspace is active,
// EditorLayout.Compute turns that + the current viewport size into exact tiling rects, and every
// panel is handed the rect it should occupy for this frame rather than positioning itself. No
// panel floats or overlaps another — see EditorLayoutTests for the geometry that guarantees it.
public sealed class UISystem : IDisposable
{
    private readonly AppConfig      _config;
    private readonly ImGuiManager   _imGui;
    private readonly StatsOverlay   _statsOverlay;
    private readonly PerformancePanel _performance;
    private readonly PropertiesPanel _properties;
    private readonly HierarchyPanel _outliner;
    private readonly TopBar         _topBar;
    private readonly TransformGizmo _gizmo;
    private readonly GizmoModeBar   _gizmoModeBar;

    // The gizmo isn't an ImGui window, so it doesn't set WantCaptureMouse — fold its own
    // hover/drag state in so InputSystem suppresses viewport picking while a handle is engaged.
    public bool WantsMouse    => _imGui.WantsMouseCapture || _gizmo.IsInteracting;
    public bool WantsKeyboard => _imGui.WantsKeyboardCapture;

    public UISystem(GL gl, AppConfig config, IWindow window, IInputContext input, ResourceSystem resourceSystem, EntitySetLoader entitySetLoader)
    {
        _config       = config;

        _imGui        = new ImGuiManager(gl, config.ImGui, window, input);

        _statsOverlay = new StatsOverlay(_imGui.Font, config);
        _performance  = new PerformancePanel(_imGui.Font, config);
        _properties   = new PropertiesPanel(_imGui.Font, _config, resourceSystem, entitySetLoader);
        _outliner     = new HierarchyPanel(_imGui.Font, resourceSystem, entitySetLoader);
        _topBar       = new TopBar(_imGui.Font, config);
        _gizmo        = new TransformGizmo();
        _gizmoModeBar = new GizmoModeBar(_imGui.Font, _gizmo);
    }

    public void Update(float deltaTime) => _imGui.Update(deltaTime);

    public void Render(Scene scene, in FrameStats stats, IReadOnlyList<GpuTiming> gpuTimings)
    {
        using var _ = Tracy.Scope("UISystem.Render");

        // Pushed every frame regardless of workspace, so switching into Performance never reveals
        // graph history with a gap in it.
        _performance.Push(in stats, gpuTimings);

        var viewport = ImGui.GetMainViewport();
        var regions  = EditorLayout.Compute(_topBar.Workspace, viewport.WorkPos, viewport.WorkSize, Widgets.FontScale);

        // Docked panels tile exactly (EditorLayout is pixel-exact), but the theme's WindowRounding
        // (Theme.cs) rounds every window's corners — against another panel that shows as a sliver of
        // background at the shared edge, and against the work-area edge it shows as a gap around the
        // outside. Docked panels are meant to read as one seamless surface, not floating cards, so
        // rounding is suppressed for exactly this block and restored after (floating windows —
        // popups, tooltips, ImGui debug windows — keep the themed rounding).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);

        using (Tracy.Scope("UISystem.Render.TopBar"))
            _topBar.Render(regions.TopBar);

        if (_config.Debug.ShowStatsOverlay && regions.Stats is { } statsRect)
            using (Tracy.Scope("UISystem.Render.StatsOverlay"))
                _statsOverlay.Render(scene, stats, statsRect);

        if (regions.Performance is { } perfRect)
            using (Tracy.Scope("UISystem.Render.Performance"))
                _performance.Render(in stats, gpuTimings, perfRect);

        // The Edit workspace's interactive panels (sidebar + left tool column + gizmo) additionally
        // need the camera to actually be in Edit mode (not Fly) — while flying, the cursor is
        // captured for camera look, so there's no mouse available to click them with anyway.
        if (regions.Outliner is { } outlinerRect && regions.Properties is { } propertiesRect && _config.Input.Mode == ViewMode.Edit)
        {
            using (Tracy.Scope("UISystem.Render.Outliner"))
                _outliner.Render(scene, outlinerRect);
            using (Tracy.Scope("UISystem.Render.Properties"))
                _properties.Render(scene, propertiesRect);

            using (Tracy.Scope("UISystem.Render.Gizmo"))
                _gizmo.Draw(scene, scene.Cameras.Active);

            if (regions.LeftTools is { } leftToolsRect)
                using (Tracy.Scope("UISystem.Render.GizmoModeBar"))
                    _gizmoModeBar.Render(leftToolsRect);
        }

        ImGui.PopStyleVar();

        using (Tracy.Scope("UISystem.Render.ImGuiFlush"))
            _imGui.Render();
    }

    public void Dispose() => _imGui.Dispose();
}
