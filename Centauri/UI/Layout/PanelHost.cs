namespace Centauri.UI.Layout;

using System.Numerics;
using ImGuiNET;

// Places an ImGui window into an exact docked LayoutRect. Shared by every docked panel
// (TopBar, GizmoModeBar, StatsOverlay, PerformancePanel, OutlinerPanel, PropertiesPanel) so they
// all use identical flags — in particular, a docked window must NOT carry AlwaysAutoResize (that
// flag overrides SetNextWindowSize and would silently break the tiling every panel here relies on).
internal static class PanelHost
{
    public const ImGuiWindowFlags DockedFlags =
        ImGuiWindowFlags.NoMove          | ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoCollapse      | ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoBringToFrontOnFocus;

    // EditorLayout's math lands every panel's far edge exactly on the work-area boundary, but the
    // ImGui/GL backend drops the last device-pixel row/column of a window that touches the true
    // screen edge (verified: interior seams between panels are pixel-exact — see
    // EditorLayoutTests and the crops in EditorLayout.md — only the outer screen edge is short by
    // one pixel). Growing every docked window's *drawn* size by a couple of pixels compensates:
    // an interior neighbour drawn afterward repaints the harmless overlap on top of it (draw order
    // in UISystem.Render), and a window's actual outer edge just bleeds a couple of pixels past
    // the true screen bound, where it's clipped by the hardware viewport instead of leaving a
    // sliver of the 3D scene visible through a gap. Callers keep using the un-bled `rect.Size` for
    // their own content layout (e.g. GizmoModeBar centering its buttons), so this only affects what
    // ImGui actually rasterizes, not panel-internal math.
    private const float OuterBleed = 2f;

    public static void Place(LayoutRect rect, float bgAlpha)
    {
        ImGui.SetNextWindowPos(rect.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(rect.Size + new Vector2(OuterBleed), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(bgAlpha);
    }
}
