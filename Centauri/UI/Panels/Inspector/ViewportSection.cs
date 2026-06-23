namespace Centauri.UI.Panels.Inspector;

using Config;
using World;
using Common;

public sealed class ViewportSection : IInspectorSection
{
    private readonly AppConfig _config;

    public ViewportSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Viewport", startCollapsed: true);
        if (!s.Open) return;

        var d = _config.Debug;   // reference type — setters mutate the shared instance

        Widgets.CheckRow("Grid",            d.ShowGrid,          v => d.ShowGrid          = v);
        Widgets.CheckRow("Skybox",          d.ShowSkybox,        v => d.ShowSkybox        = v);
        Widgets.CheckRow("Stats Overlay",   d.ShowStatsOverlay,  v => d.ShowStatsOverlay  = v);
        Widgets.CheckRow("Frustum Culling", d.EnableCulling,     v => d.EnableCulling     = v);

        Widgets.CheckRow("Bounding Boxes",  d.ShowBoundingBoxes, v => d.ShowBoundingBoxes = v);
        Widgets.CheckRow("Cameras",         d.ShowCameras,       v => d.ShowCameras       = v);
        Widgets.CheckRow("Frustums",        d.ShowFrustums,      v => d.ShowFrustums      = v);
    }
}