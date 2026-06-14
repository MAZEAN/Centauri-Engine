namespace Centauri.Rendering.UI;

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
    
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoMove                |
                                           ImGuiWindowFlags.NoSavedSettings       |
                                           ImGuiWindowFlags.NoBringToFrontOnFocus |
                                           ImGuiWindowFlags.AlwaysAutoResize;

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

        if (!ImGui.Begin("StatsOverlay", Flags))
        {
            ImGui.End();
            return;
        }

        var cam = scene.GetActiveCamera();
        ImGui.PushFont(_font);

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

        ImGui.PopFont();
        ImGui.End();
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
        bool open = GUI.BeginPanel(title, accent);   // colored, collapsible header
        if (open) rows();
        GUI.EndPanel(open);
    }

    private static void Row(string label, string value) =>
        GUI.TextRow(label, value);

    private static void RowColored(string label, string value, Vector4 color) =>
        GUI.TextRow(label, value, color);

    private static void ConfigRow(string label, bool value) =>
        GUI.TextRow(label, value.ToString(), GUI.Bool(value));
}