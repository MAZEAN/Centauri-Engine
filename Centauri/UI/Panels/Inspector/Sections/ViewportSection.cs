namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

public sealed class ViewportSection : ISection
{
    private readonly DebugConfig  _config;
    private readonly RenderConfig _render;

    public ViewportSection(DebugConfig config, RenderConfig render)
    {
        _config = config;
        _render = render;
    }

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Viewport", startCollapsed: true);
        if (!s.Open) return;

        Widgets.DragRow("Render Scale", _render.RenderScale, v => _render.RenderScale = v,
            0.05f, 0.1f, 1f, "%.2f", 1f);
        Widgets.CheckRow("Grid",            _config.ShowGrid,          v => _config.ShowGrid          = v);
        Widgets.CheckRow("Skybox",          _config.ShowSkybox,        v => _config.ShowSkybox        = v);
        Widgets.CheckRow("Stats Overlay",   _config.ShowStatsOverlay,  v => _config.ShowStatsOverlay  = v);
        Widgets.CheckRow("Frustum Culling", _config.EnableCulling,     v => _config.EnableCulling     = v);
        Widgets.CheckRow("Z-Prepass",       _config.EnableZPrepass,   v => _config.EnableZPrepass    = v);
        Widgets.CheckRow("Anisotropic",     _config.AnisotropicFilter, v => _config.AnisotropicFilter = v);
        Widgets.CheckRow("Bounding Boxes",  _config.ShowBoundingBoxes, v => _config.ShowBoundingBoxes = v);
        Widgets.CheckRow("Spatial Grid",    _config.ShowCullingGrid,   v => _config.ShowCullingGrid   = v);
        Widgets.CheckRow("Cameras",         _config.ShowCameras,       v => _config.ShowCameras       = v);
        Widgets.CheckRow("Frustums",        _config.ShowFrustums,      v => _config.ShowFrustums      = v);
    }
}