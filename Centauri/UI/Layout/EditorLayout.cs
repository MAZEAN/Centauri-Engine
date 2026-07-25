namespace Centauri.UI.Layout;

using System.Numerics;

// A resolved screen-space slot for a panel. Left/Top/Right/Bottom are convenience accessors used
// heavily by the tiling tests (EditorLayoutTests) to assert exact shared edges between neighbours.
internal readonly record struct LayoutRect(Vector2 Pos, Vector2 Size)
{
    public float Left   => Pos.X;
    public float Top    => Pos.Y;
    public float Right  => Pos.X + Size.X;
    public float Bottom => Pos.Y + Size.Y;
}

// Which task the editor is set up for — decided by ModeManager.Workspace, a storage concept
// EditorLayout itself knows nothing about (it just takes whichever value Compute is called with).
// ModeManager keeps this loosely coupled to Config.ViewMode (Fly vs. Edit/pick camera behavior) —
// see its own comments for the coupling — rather than EditorLayout knowing about camera state at all.
internal enum EditorWorkspace { Edit, Performance, Viewing }

// Every docked region for a given workspace at a given resolution. Optional slots are null when
// that workspace doesn't show them (e.g. Viewing has nothing but TopBar + Viewport).
internal readonly record struct EditorRegions
{
    public LayoutRect  TopBar      { get; init; }
    public LayoutRect  Viewport    { get; init; }
    public LayoutRect? LeftTools   { get; init; }
    public LayoutRect? Outliner    { get; init; }
    public LayoutRect? Properties  { get; init; }
    public LayoutRect? Stats       { get; init; }
    public LayoutRect? Performance { get; init; }
}

// Turns (workspace, work-area rect, UI scale) into exact docked rects — pure geometry, no ImGui
// calls, no mutable state, so the whole arrangement is one function that's actually unit-testable
// (see Centauri.Tests/UI/EditorLayoutTests.cs) instead of positioning logic smeared across every
// panel's own SetupWindow. `uiScale` mirrors Widgets.FontScale (design-pixel constants below are
// multiplied by it), passed in rather than read from Widgets' static state so this stays a pure
// function of its arguments.
//
// The Edit workspace's five regions (TopBar, LeftTools, Outliner, Properties, Viewport) are built
// from one shared set of intermediate values (bodyY/bodyH, toolColW, sidebarW, viewportW) rather
// than each computed independently — every neighbour's shared edge is therefore the *same*
// floating-point value, not two independently-rounded ones that happen to be close, which is what
// makes the "no gaps, no overlaps" tiling exact instead of approximate.
internal static class EditorLayout
{
    private const float TopBarH        = 38f;
    private const float ToolColW       = 46f;   // one icon button (34) + padding (6 each side)
    private const float SidebarW       = 320f;
    private const float OutlinerFrac   = 0.35f; // fraction of sidebar height given to the entity list
    private const float MinViewportW   = 100f;  // floor so a docked sidebar can never fully swallow it
    // The perf graphs column is sized relative to Stats' width rather than eating whatever's left —
    // a fixed multiple keeps it proportioned instead of stretching edge-to-edge on an ultrawide
    // monitor, which is what the "downscale the graphs" ask was really about.
    private const float PerfGraphWidthScale = 1.15f;

    public static EditorRegions Compute(EditorWorkspace workspace, Vector2 workPos, Vector2 workSize, float uiScale)
    {
        var scale = MathF.Max(uiScale, 0.01f); // guard against a degenerate/zero scale
        var w     = MathF.Max(workSize.X, 0f);
        var h     = MathF.Max(workSize.Y, 0f);

        var topBarH = MathF.Min(TopBarH * scale, h);
        var topBar  = new LayoutRect(workPos, new Vector2(w, topBarH));

        var bodyY = workPos.Y + topBarH;
        var bodyH = h - topBarH; // >= 0 since topBarH was clamped to <= h

        return workspace switch
        {
            EditorWorkspace.Edit        => ComputeEdit(workPos, w, bodyY, bodyH, scale, topBar),
            EditorWorkspace.Performance => ComputePerformance(workPos, w, bodyY, bodyH, scale, topBar),
            _                           => new EditorRegions
            {
                TopBar   = topBar,
                Viewport = new LayoutRect(new Vector2(workPos.X, bodyY), new Vector2(w, bodyH)),
            },
        };
    }

    private static EditorRegions ComputeEdit(Vector2 workPos, float w, float bodyY, float bodyH, float scale, LayoutRect topBar)
    {
        // Tool column first (small, effectively never clamped), then the sidebar takes whatever's
        // left after reserving MinViewportW for the render — clamped down (never negative) rather
        // than letting the viewport go negative-width on a very small window.
        var toolColW = MathF.Min(ToolColW * scale, w);
        var avail    = w - toolColW;                                   // width left for sidebar + viewport
        var minVpW   = MathF.Min(MinViewportW * scale, avail);         // can't reserve more than exists
        var sidebarW  = Math.Clamp(SidebarW * scale, 0f, avail - minVpW);
        var viewportW = avail - sidebarW;

        var sidebarX  = workPos.X + toolColW + viewportW;
        var outlinerH = bodyH * OutlinerFrac;

        return new EditorRegions
        {
            TopBar     = topBar,
            LeftTools  = new LayoutRect(new Vector2(workPos.X, bodyY), new Vector2(toolColW, bodyH)),
            Viewport   = new LayoutRect(new Vector2(workPos.X + toolColW, bodyY), new Vector2(viewportW, bodyH)),
            Outliner   = new LayoutRect(new Vector2(sidebarX, bodyY), new Vector2(sidebarW, outlinerH)),
            Properties = new LayoutRect(new Vector2(sidebarX, bodyY + outlinerH), new Vector2(sidebarW, bodyH - outlinerH)),
        };
    }

    private static EditorRegions ComputePerformance(Vector2 workPos, float w, float bodyY, float bodyH, float scale, LayoutRect topBar)
    {
        // Two full-height columns on the left — Stats, then the perf graphs immediately beside it —
        // mirroring Edit's sidebar shape (just flipped to the left edge, and two columns instead of
        // a stacked sidebar, since neither panel wants to share width with the other). The scene
        // stays visible, filling whatever's left to the right, rather than being replaced outright
        // (an earlier cut did that) or squeezed into a short bottom strip (a later one did that,
        // and cut the GPU-timing graph off at the bottom — full body height fixes that outright
        // instead of guessing at a tall-enough fixed strip height).
        var minViewportW = MathF.Min(MinViewportW * scale, w);
        var avail        = w - minViewportW; // width left for Stats + graphs, viewport floor reserved
        var statsW       = Math.Clamp(SidebarW * scale, 0f, avail);
        var perfW        = Math.Clamp(statsW * PerfGraphWidthScale, 0f, avail - statsW);
        var viewportW    = w - statsW - perfW;

        return new EditorRegions
        {
            TopBar      = topBar,
            Stats       = new LayoutRect(new Vector2(workPos.X, bodyY), new Vector2(statsW, bodyH)),
            Performance = new LayoutRect(new Vector2(workPos.X + statsW, bodyY), new Vector2(perfW, bodyH)),
            Viewport    = new LayoutRect(new Vector2(workPos.X + statsW + perfW, bodyY), new Vector2(viewportW, bodyH)),
        };
    }
}
