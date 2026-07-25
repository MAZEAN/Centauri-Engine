namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

// Per-panel background opacity (PanelHost.Place's bgAlpha — see EditorLayout.md §4). Floored at
// 0.2 rather than 0: a panel driven fully transparent (this one included, since Properties is
// itself one of the panels it controls) would leave nothing to click to bring it back up.
public sealed class PanelAppearanceSection : ISection
{
    private const float MinAlpha = 0.2f;

    private readonly ImGuiConfig _config;

    public PanelAppearanceSection(ImGuiConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Panel Transparency", startCollapsed: true);
        if (!s.Open) return;

        Widgets.DragRow("Top Bar",     _config.TopBarAlpha,      v => _config.TopBarAlpha      = v, 0.01f, MinAlpha, 1f, "%.2f", 1f);
        Widgets.DragRow("Left Tools",  _config.LeftToolsAlpha,   v => _config.LeftToolsAlpha   = v, 0.01f, MinAlpha, 1f, "%.2f", 1f);
        Widgets.DragRow("Outliner",    _config.OutlinerAlpha,    v => _config.OutlinerAlpha    = v, 0.01f, MinAlpha, 1f, "%.2f", 1f);
        Widgets.DragRow("Properties",  _config.PropertiesAlpha,  v => _config.PropertiesAlpha  = v, 0.01f, MinAlpha, 1f, "%.2f", 1f);
        Widgets.DragRow("Statistics",  _config.StatsAlpha,       v => _config.StatsAlpha       = v, 0.01f, MinAlpha, 1f, "%.2f", 1f);
        Widgets.DragRow("Performance", _config.PerformanceAlpha, v => _config.PerformanceAlpha = v, 0.01f, MinAlpha, 1f, "%.2f", 1f);
    }
}
