namespace Centauri.UI.Panels.Graphs;

using ImGuiNET;
using System.Numerics;

using Common;

// Rolling FPS history drawn as a labeled line graph. Samples are averaged over
// a fixed time interval (not per-frame) so the window stays steady whether the
// engine runs at 30 FPS or 2500+. The Y axis auto-scales to a tidy ceiling above
// the visible peak, so very high frame rates still fit on a clean axis.
internal sealed class PerformanceGraph
{
    private const float GraphHeight = 150f;
    private const float LeftPad = 30f;   // Y tick-label gutter
    private const float BotPad  = 20f;   // X label gutter
    private const float TopPad  = 20f;
    
    private const int   Capacity         = 200;    // samples retained
    private const float SampleIntervalMs = 50f;    // one plotted point per 50 ms
    private const float WindowSeconds    = Capacity * SampleIntervalMs / 1000f;

    private readonly float[] _samples = new float[Capacity];
    private int _head;     // next write slot
    private int _count;    // valid samples (≤ Capacity)

    // accumulator for interval averaging
    private float _accMs;
    private float _accFps;
    private int   _accN;

    public void Push(float fps, float frameTimeMs)
    {
        _accFps += fps;
        _accN++;
        _accMs += frameTimeMs;
        if (_accMs < SampleIntervalMs) return;

        _samples[_head] = _accN > 0 ? _accFps / _accN : fps;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;

        _accMs = 0f;
        _accFps = 0f;
        _accN = 0;
    }

    public void Draw()
    {
        var avail  = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(avail, GraphHeight));    // reserve layout space

        var p0 = new Vector2(origin.X + LeftPad, origin.Y + TopPad);
        var p1 = new Vector2(origin.X + avail,   origin.Y + GraphHeight - BotPad);
        var w  = p1.X - p0.X;
        var h  = p1.Y - p0.Y;
        if (w <= 1f || h <= 1f) return;

        var dl = ImGui.GetWindowDrawList();

        var bg   = ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.10f, 0.85f));
        var edge = ImGui.GetColorU32(new Vector4(0.30f, 0.30f, 0.30f, 1f));
        var grid = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f));
        var tick = ImGui.GetColorU32(new Vector4(0.70f, 0.70f, 0.70f, 1f));
        var line = ImGui.GetColorU32(ColorPalette.Amber);

        dl.AddRectFilled(p0, p1, bg);
        dl.AddRect(p0, p1, edge);

        // ── Y scale + gridlines ──────────────────────────────────────────────
        var peak = 1f;
        for (var i = 0; i < _count; i++) peak = MathF.Max(peak, _samples[i]);
        var yMax = NiceCeil(peak);

        const int divs = 5;
        for (var d = 0; d <= divs; d++)
        {
            var t = d / (float)divs;
            var y = p1.Y - t * h;
            dl.AddLine(new Vector2(p0.X, y), new Vector2(p1.X, y), grid);

            var label = FormatTick(yMax * t);
            var sz = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(p0.X - 6f - sz.X, y - sz.Y * 0.5f), tick, label);
        }

        // ── series ────────────────────────────────────────────────────────────
        if (_count >= 2)
        {
            var prev = Vector2.Zero;
            for (var i = 0; i < _count; i++)
            {
                var idx = (_head - _count + i + Capacity) % Capacity;  // chronological
                var x = p0.X + w * (i / (float)(_count - 1));
                var y = p1.Y - h * MathF.Min(_samples[idx] / yMax, 1f);
                var cur = new Vector2(x, y);
                if (i > 0) dl.AddLine(prev, cur, line, 1.5f);
                prev = cur;
            }
        }

        // ── X labels + current value ───────────────────────────────────────────
        dl.AddText(new Vector2(p0.X, p1.Y + 2f), tick, $"-{WindowSeconds:0}s");
        var nowSz = ImGui.CalcTextSize("now");
        dl.AddText(new Vector2(p1.X - nowSz.X, p1.Y + 2f), tick, "now");

        if (_count > 0)
        {
            var current = _samples[(_head - 1 + Capacity) % Capacity];
            var s = $"{current:0} FPS";
            var sz = ImGui.CalcTextSize(s);
            dl.AddText(new Vector2(p1.X - sz.X - 4f, p0.Y + 2f), line, s);
        }
    }

    // Round up to a tidy axis maximum (… 1, 2, 2.5, 5 × 10ⁿ …), min 60.
    private static float NiceCeil(float v)
    {
        if (v <= 60f) return 60f;
        
        var mag  = MathF.Pow(10f, MathF.Floor(MathF.Log10(v)));
        var n    = v / mag;
        var nice = n <= 1f ? 1f : n <= 2f ? 2f : n <= 2.5f ? 2.5f : n <= 5f ? 5f : 10f;
        return nice * mag;
    }

    private static string FormatTick(float v) => v >= 1000f ? $"{v / 1000f:0.#}k" : $"{v:0}";
}