namespace Centauri.UI.Panels.Inspector.Sections;

using Config;
using World;
using Common;

// Spatial-culling grid controls. Cell Size is the XZ size of a grid cell; Oversize is the
// multiple of cell size above which an entity (ground/terrain) bypasses the grid and is
// always tested. Changing either retunes the grid live; live counts are in the stats overlay.
public sealed class CullingSection : ISection
{
    private readonly AppConfig _config;

    public CullingSection(AppConfig config) => _config = config;

    public void Draw(Scene scene)
    {
        using var s = Widgets.Section("Culling", startCollapsed: true);
        if (!s.Open) return;

        var dbg  = _config.Debug;
        var conf = _config.Culling;

        Widgets.CheckRow("Enabled", dbg.EnableCulling, v => dbg.EnableCulling = v);

        Widgets.DragRow("Cell Size", conf.CellSize, v => conf.CellSize = MathF.Max(1f, v),
            0.5f, 1f, 256f, "%.1f", conf.AuthoredCellSize);
        Widgets.DragRow("Oversize x", conf.OversizeFactor, v => conf.OversizeFactor = MathF.Max(1f, v),
            0.25f, 1f, 64f, "%.2f", conf.AuthoredOversizeFactor);
    }
}