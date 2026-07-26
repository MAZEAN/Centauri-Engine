namespace Centauri.UI.Panels.Stats;

using ImGuiNET;
using System.Numerics;

using Utils.Misc;
using Config;
using Common;
using Layout;
using Rendering.Profiling;
using Graphs;

// The frame-time and GPU-timing graphs — the Performance workspace's whole reason to exist. They
// were originally embedded in StatsOverlay's "Performance" section, but graphs are the one thing in
// that panel that actually wants width: squeezed into a narrow sidebar the history is unreadable.
// Splitting them into their own panel lets the Performance workspace give them the width they need
// (see EditorLayout.ComputePerformance) instead of competing with a table of instantaneous numbers.
internal sealed class PerformancePanel
{
    private readonly ImFontPtr _font;
    private readonly AppConfig _config;

    private readonly PerformanceGraph _perfGraph = new();
    private readonly GPUTimingGraph   _gpuGraph  = new();

    public PerformancePanel(ImFontPtr font, AppConfig config)
    {
        _font   = font;
        _config = config;
    }

    // Pushed every frame regardless of whether the panel is currently shown, so switching into the
    // Performance workspace never reveals a graph with a gap in its history.
    public void Push(in FrameStats stats, IReadOnlyList<GpuTiming> gpuTimings)
    {
        _perfGraph.Push(stats.FPS, stats.FrameTime);
        if (gpuTimings.Count > 0)
            _gpuGraph.Push(gpuTimings, stats.FrameTime);
    }

    public void Render(in FrameStats stats, IReadOnlyList<GpuTiming> gpuTimings, LayoutRect rect)
    {
        PanelHost.Place(rect, bgAlpha: _config.ImGui.PerformanceAlpha);

        if (!ImGui.Begin("Performance", PanelHost.DockedFlags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);

        var gpu = _config.Debug.ShowGPUTimings;
        if (ImGui.Checkbox("GPU Timings", ref gpu))
            _config.Debug.ShowGPUTimings = gpu;

        var showGpu = gpu && gpuTimings.Count > 0;

        // Stacked vertically rather than side-by-side: each graph plots against wall-clock time on
        // its X axis, so the two only compare meaningfully when they share the same width/timescale
        // — splitting the panel into side-by-side columns halves both instead.
        _perfGraph.Draw();
        if (showGpu)
            _gpuGraph.Draw();

        ImGui.PopFont();
        ImGui.End();
    }
}
