namespace Centauri.UI.Panels.Stats;

using ImGuiNET;
using System.Numerics;

using Utils.Misc;
using World;
using Config;
using Common;
using Layout;
using Graphics.Resources;

// Instantaneous engine statistics (frame time headline, renderer/culling/shadow/instancing/physics/
// camera/config counters). The frame-time and GPU-timing *graphs* used to live here too, but they're
// the one thing here that wants width — they're their own panel now (PerformancePanel), shown in the
// dedicated Performance workspace where they can actually have room. See EditorLayout.cs.
internal sealed class StatsOverlay
{
    private const float LabelWidth = 120f;

    private readonly ImFontPtr _font;
    private readonly AppConfig _config;

    public StatsOverlay(ImFontPtr font, AppConfig config)
    {
        _font   = font;
        _config = config;
    }

    public void Render(Scene scene, FrameStats stats, LayoutRect rect)
    {
        PanelHost.Place(rect, bgAlpha: _config.ImGui.StatsAlpha);

        var flags = PanelHost.DockedFlags;
        if (_config.Input.Mode == ViewMode.Fly)
            flags |= ImGuiWindowFlags.NoInputs; // don't eat clicks meant for camera look while flying

        if (!ImGui.Begin("Statistics", flags))
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
        });

        Section("Renderer", ColorPalette.Blue, () =>
        {
            Row("Draw Calls",     stats.DrawCalls.ToString());
            Row("Texture Binds",  stats.TextureBinds.ToString());
            Row("Total Indices",  stats.TotalIndices.ToString());
            Row("Total Vertices", stats.TotalVertices.ToString());
            Row("Triangles",      (stats.TotalIndices / 3).ToString());
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
            Row("Ratio",    $"{Widgets.Float(ratio)} %");
            Row("Grid",     $"{stats.GridColumns}x{stats.GridRows} ({stats.GridCells})");
            Row("Occupied", stats.GridOccupied.ToString());
            Row("Visited",  stats.GridVisited.ToString());
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

        Section("Instancing", ColorPalette.Green, () =>
        {
            Row("Batches",       stats.Batches.ToString());
            Row("Instances",     stats.DrawnEntities.ToString());
            RowColored("Inst/Draw", Widgets.Float(stats.InstancesPerDraw),
                stats.InstancesPerDraw > 1f ? ColorPalette.Green : ColorPalette.White);
            RowColored("Saved", $"{Widgets.Float(stats.DrawCallReduction)} %",
                stats.DrawCallReduction > 0f ? ColorPalette.Green : ColorPalette.White);
            Row("Two-Sided", $"{stats.TwoSidedEntities}/{stats.RenderableEntities} ({Widgets.Float(stats.TwoSidedPercent)} %)");
        });

        if (_config.Physics.Enabled)
        {
            Section("Physics", ColorPalette.Green, () =>
            {
                Row("Dynamic Bodies", stats.PhysicsDynamicBodies.ToString());
                Row("Static Bodies",  stats.PhysicsStaticBodies.ToString());
                Row("Steps/Frame",    stats.PhysicsStepsThisFrame.ToString());
                Row("Step Time",      $"{Widgets.Float(stats.PhysicsStepMsThisFrame, 3)} ms");
            });
        }

        var cam = scene.Cameras.Active;
        Section("Camera", ColorPalette.Red, () =>
        {
            RowColored("Active",   cam.Name, ColorPalette.Amber);
            RowColored("Position", Widgets.Vec3(cam.Position), ColorPalette.Blue);
            RowColored("Forward",  Widgets.Vec3(cam.Forward),  ColorPalette.Green);
            Row("Yaw",   Widgets.SignedFloat(cam.Yaw));
            Row("Pitch", Widgets.SignedFloat(cam.Pitch));
            Row("Zoom",  Widgets.Float(cam.Zoom));
        });

        Section("Textures", ColorPalette.Amber, () =>
        {
            var compressed   = GLTexture.CompressedTextureCount;
            var uncompressed = GLTexture.UncompressedTextureCount;
            RowColored("Compressed", compressed.ToString(),
                compressed > 0 ? ColorPalette.Green : ColorPalette.White);
            Row("Uncompressed", uncompressed.ToString());
            Row("Est. VRAM", $"{GLTexture.TotalApproxBytes / (1024f * 1024f):F1} MB");
        });

        Section("Config", ColorPalette.Purple, () =>
        {
            ConfigRow("VSync",   _config.Window.EnableVSync);
            ConfigRow("Culling", _config.Debug.EnableCulling);
        });
    }

    private static void Section(string title, Vector4 accent, Action rows, bool defaultOpen = true)
    {
        var open = Widgets.BeginPanel(title, accent, defaultOpen);   // colored, collapsible header
        if (open)
            rows();
        Widgets.EndPanel(open);
    }

    private static void Row(string label, string value) =>
        StatRow(label, value, default);

    private static void RowColored(string label, string value, Vector4 color) =>
        StatRow(label, value, color);

    private static void ConfigRow(string label, bool value) =>
        StatRow(label, value.ToString(), Widgets.BooleanColor(value));

    private static void StatRow(string label, string value, Vector4 color)
    {
        var startX = ImGui.GetCursorPosX();
        var textW  = ImGui.CalcTextSize(label).X;

        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        // MathF.Max, not the scaled width alone: a label longer than the design width (bigger
        // font, or just a long label) must still get pushed past its own text instead of the
        // value starting mid-label — this is what threw when it didn't (see GPUTimingGraph).
        ImGui.SetCursorPosX(startX + MathF.Max(Widgets.Scale(LabelWidth), textW + Widgets.Scale(8f)));

        var tinted = color.W > 0f;
        if (tinted)
            ImGui.PushStyleColor(ImGuiCol.Text, color);

        ImGui.TextUnformatted(value);
        if (tinted)
            ImGui.PopStyleColor();
    }
}
