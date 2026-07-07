namespace Centauri.UI.Panels.Stats;

using ImGuiNET;
using System.Numerics;

using Utils.Misc;
using World;
using Config;
using Common;
using Rendering.Profiling;
using Graphs;

public class StatsOverlay
{
    private const float Width      = 350f;
    private const float Padding    = 10f;
    private const float BgAlpha    = 0.85f;
    private const float LabelWidth = 120f;
    
    private const ImGuiWindowFlags Flags = Widgets.PanelBase | ImGuiWindowFlags.NoBringToFrontOnFocus; 

    private readonly ImFontPtr _font;
    private readonly AppConfig _config;
    
    private readonly PerformanceGraph _perfGraph = new();
    private readonly GPUTimingGraph _gpuGraph = new();

    public StatsOverlay(ImFontPtr font, AppConfig config)
    {
        _font   = font;
        _config = config;
    }

    public void Render(Scene scene, FrameStats stats, IReadOnlyList<GpuTiming> gpuTimings)
    {
        _perfGraph.Push(stats.FPS, stats.FrameTime);
        SetupWindow();

        if (!ImGui.Begin("StatsOverlay", GetModeDependentFlags(_config.Input.Mode)))
        {
            ImGui.End();
            return;
        }
        
        ImGui.PushFont(_font);
        
        DrawSections(scene, stats, gpuTimings);

        ImGui.PopFont();
        ImGui.End();
    }

    private void DrawSections(Scene scene, FrameStats stats, IReadOnlyList<GpuTiming> gpuTimings)
    {
        Section("Performance", ColorPalette.Amber, () =>
        {
            Row("FPS", Widgets.Float(stats.FPS));
            Row("Frame Time", $"{Widgets.Float(stats.FrameTime)} ms");
            _perfGraph.Draw();
            
            var gpu = _config.Debug.ShowGPUTimings;
            if (ImGui.Checkbox("GPU Timings", ref gpu))
                _config.Debug.ShowGPUTimings = gpu;
        });

        if (gpuTimings.Count > 0)
        {
            _gpuGraph.Push(gpuTimings, stats.FrameTime);
            Section("GPU (ms)", ColorPalette.Amber, () => _gpuGraph.Draw());
        }

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

        Section("Renderer", ColorPalette.Blue, () =>
        {
            Row("Draw Calls",     stats.DrawCalls.ToString());
            Row("Texture Binds",  stats.TextureBinds.ToString());
            Row("Total Indices",  stats.TotalIndices.ToString());
            Row("Total Vertices", stats.TotalVertices.ToString());
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

        Section("Config", ColorPalette.Purple, () =>
        {
            ConfigRow("VSync",   _config.Window.EnableVSync);
            ConfigRow("Culling", _config.Debug.EnableCulling);
        }, defaultOpen:false);
    }

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();
        var padding = Widgets.Scale(Padding);
        var width   = Widgets.Scale(Width);
        var anchor  = new Vector2(viewport.WorkPos.X + padding, viewport.WorkPos.Y + padding);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(0f, 0f));
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(width, 0),
            new Vector2(width, float.MaxValue));
        ImGui.SetNextWindowBgAlpha(BgAlpha);
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

    private static ImGuiWindowFlags GetModeDependentFlags(ViewMode activeMode)
    {
        var flags = Flags;
        if (activeMode == ViewMode.Fly)
            flags |= ImGuiWindowFlags.NoInputs;
        
        return flags;
    }
}