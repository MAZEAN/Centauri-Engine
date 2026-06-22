namespace Centauri.UI.Panels;

using ImGuiNET;
using System.Numerics;

using Utils.Misc;
using World;
using Config;
using Common;

public class StatsOverlay
{
    private const int   Width   = 350;
    private const float Padding = 10f;
    private const float BgAlpha = 0.85f;
    private const float LabelWidth = 120f;
    
    private const ImGuiWindowFlags Flags = Widgets.PanelBase | ImGuiWindowFlags.NoBringToFrontOnFocus; 

    private readonly ImFontPtr _font;
    private readonly AppConfig _config;
    private readonly PerformanceGraph _perfGraph = new();

    public StatsOverlay(ImFontPtr font, AppConfig config)
    {
        _font   = font;
        _config = config;
    }

    public void Render(Scene scene, FrameStats stats)
    {
        _perfGraph.Push(stats.FPS, stats.FrameTime);
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
        Section("Performance", ColorPalette.Amber, () =>
        {
            Row("FPS", Widgets.Float(stats.FPS));
            Row("Frame Time", $"{Widgets.Float(stats.FrameTime)} ms");
            _perfGraph.Draw();
        });

        Section("Culling", ColorPalette.Green, () =>
        {
            Row("Total", stats.TotalEntities.ToString());
            RowColored("Drawn", stats.DrawnEntities.ToString(),
                stats.CulledEntities > 0 ? ColorPalette.Green : ColorPalette.White);
            RowColored("Culled", stats.CulledEntities.ToString(), ColorPalette.Red);

            var ratio = stats.TotalEntities > 0
                ? stats.CulledEntities / (float)stats.TotalEntities * 100f
                : 0f;
            Row("Ratio", $"{Widgets.Float(ratio)} %");
        });
        
        Section("Shadows", ColorPalette.Blue, () =>
        {
            Row("Cascades", _config.Shadows.CascadeCount.ToString());
            Row("Casters",  stats.ShadowCasters.ToString());
            RowColored("Culled", stats.ShadowCulled.ToString(), ColorPalette.Red);

            var ratio = stats.ShadowTotal > 0
                ? stats.ShadowCulled / (float)stats.ShadowTotal * 100f
                : 0f;
            Row("Ratio", $"{Widgets.Float(ratio)} %");
        });

        Section("Renderer", ColorPalette.Blue, () =>
        {
            Row("Draw Calls",     stats.DrawCalls.ToString());
            Row("Texture Binds",  stats.TextureBinds.ToString());
            Row("Total Indices",  stats.TotalIndices.ToString());
            Row("Total Vertices", stats.TotalVertices.ToString());
        });
        
        var cam = scene.Cameras.Active;
        Section("Camera", ColorPalette.Red, () =>
        {
            RowColored("Active", cam.Name, ColorPalette.Amber);
            RowColored("Position", Widgets.Vec3(cam.Position), ColorPalette.Blue);
            RowColored("Forward",  Widgets.Vec3(cam.Forward),  ColorPalette.Green);
            Row("Yaw",   Widgets.SignedFloat(cam.Yaw));
            Row("Pitch", Widgets.SignedFloat(cam.Pitch));
            Row("Zoom",  Widgets.Float(cam.Zoom));
        });

        Section("Config", ColorPalette.Purple, () =>
        {
            ConfigRow("VSync",   _config.Window.EnableVSync);
            ConfigRow("Culling", _config.Debug.EnableCulling);
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
        var open = Widgets.BeginPanel(title, accent);   // colored, collapsible header
        if (open) rows();
        
        Widgets.EndPanel(open);
    }

    private static void Row(string label, string value) =>
        StatRow(label, value, default);

    private static void RowColored(string label, string value, Vector4 color) =>
        StatRow(label, value, color);

    private static void ConfigRow(string label, bool value) =>
        StatRow(label, value.ToString(), Widgets.BooleanColor(value));

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