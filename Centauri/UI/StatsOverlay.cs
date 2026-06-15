namespace Centauri.UI;

using ImGuiNET;
using System.Numerics;

using Utils.Misc;
using World;
using Config;

public class StatsOverlay
{
    private const int   Width   = 350;
    private const float Padding = 10f;
    private const float BgAlpha = 0.85f;
    private const float LabelWidth = 120f;
    
    private const ImGuiWindowFlags Flags = GUI.PanelBase | ImGuiWindowFlags.NoBringToFrontOnFocus; 

    private readonly ImFontPtr _font;
    private readonly AppConfig _config;

    public StatsOverlay(ImFontPtr font, AppConfig config)
    {
        _font   = font;
        _config = config;
    }

    public void Render(Scene scene, FrameStats stats)
    {
        SetupWindow();

        if (!ImGui.Begin("StatsOverlay", GetModeDependentFlags(_config.Input.Mode)))
        {
            ImGui.End();
            return;
        }
        
        ImGui.PushFont(_font);

        DrawSections(scene, stats);

        ImGui.PopFont();
        ImGui.End();
    }

    private void DrawSections(Scene scene, FrameStats stats)
    {
        Section("Performance",GUI.Amber, () =>
        {
            Row("FPS", GUI.Float(stats.FPS));
            Row("Frame Time", $"{GUI.Float(stats.FrameTime)} ms");
        });

        Section("Culling",GUI.Green, () =>
        {
            Row("Total", stats.TotalEntities.ToString());
            RowColored("Drawn", stats.DrawnEntities.ToString(),
                stats.CulledEntities > 0 ? GUI.Green : GUI.White);
            RowColored("Culled", stats.CulledEntities.ToString(),GUI.Red);

            var ratio = stats.TotalEntities > 0
                ? stats.CulledEntities / (float)stats.TotalEntities * 100f
                : 0f;
            Row("Ratio", $"{GUI.Float(ratio)} %");
        });

        Section("Renderer",GUI.Blue, () =>
        {
            Row("Draw Calls",     stats.DrawCalls.ToString());
            Row("Texture Binds",  stats.TextureBinds.ToString());
            Row("Total Indices",  stats.TotalIndices.ToString());
            Row("Total Vertices", stats.TotalVertices.ToString());
        });
        
        var cam = scene.Cameras.Active;
        Section("Camera",GUI.Red, () =>
        {
            RowColored("Active", cam.Name,GUI.Amber);
            RowColored("Position", GUI.Vec3(cam.Position), GUI.Blue);
            RowColored("Forward",  GUI.Vec3(cam.Forward),  GUI.Green);
            Row("Yaw",   GUI.SignedFloat(cam.Yaw));
            Row("Pitch", GUI.SignedFloat(cam.Pitch));
            Row("Zoom",  GUI.Float(cam.Zoom));
        });

        Section("Config",GUI.Purple, () =>
        {
            RowColored("ViewMode", _config.Input.Mode.ToString(),GUI.Amber);
            ConfigRow("VSync",         _config.Window.EnableVSync);
            ConfigRow("Culling",       _config.Debug.EnableCulling);
            ConfigRow("DebugView",     _config.Debug.ShowDebugView);
            ConfigRow("BoundingBoxes", _config.Debug.ShowBoundingBoxes);
            ConfigRow("Frustums",      _config.Debug.ShowFrustums);
            ConfigRow("Cameras",       _config.Debug.ShowCameras);
            ConfigRow("Grid",          _config.Debug.ShowGrid);
        });
    }

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();
        var anchor = new Vector2(viewport.WorkPos.X + Padding, viewport.WorkPos.Y + Padding);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(0f, 0f));
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(Width, 0),
            new Vector2(Width, float.MaxValue));
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }

    private static void Section(string title, Vector4 accent, Action rows)
    {
        var open = GUI.BeginPanel(title, accent);   // colored, collapsible header
        if (open) rows();
        
        GUI.EndPanel(open);
    }

    private static void Row(string label, string value) =>
        StatRow(label, value, default);

    private static void RowColored(string label, string value, Vector4 color) =>
        StatRow(label, value, color);

    private static void ConfigRow(string label, bool value) =>
        StatRow(label, value.ToString(), GUI.Bool(value));

    // Left-aligned label in a fixed column, value follows on the same line.
    private static void StatRow(string label, string value, Vector4 color)
    {
        var startX = ImGui.GetCursorPosX();
        
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.SetCursorPosX(startX + LabelWidth);

        var tinted = color.W > 0f;
        if (tinted) ImGui.PushStyleColor(ImGuiCol.Text, color);
        
        ImGui.TextUnformatted(value);
        if (tinted) ImGui.PopStyleColor();
    }

    private static ImGuiWindowFlags GetModeDependentFlags(ViewMode activeMode)
    {
        var flags = Flags;
        if (activeMode == ViewMode.Fly)
            flags |= ImGuiWindowFlags.NoInputs;
        return flags;
    }
}