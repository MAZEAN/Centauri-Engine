namespace Centauri.UI.Layout;

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

    public static void Place(LayoutRect rect, float bgAlpha)
    {
        ImGui.SetNextWindowPos(rect.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(rect.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(bgAlpha);
    }
}
