namespace Centauri.UI.Panels.Graphs;

using ImGuiNET;
using System.Numerics;

using Common;

internal sealed class PerformanceGraph
{
    private const float GraphHeight = 150f;
    private const float LeftPad = 30f;   // Y tick-label gutter
    private const float BotPad  = 20f;   // X label gutter
    private const float TopPad  = 20f;
    
    private const int   Capacity         = 200;    // samples retained
    private const float SampleIntervalMs = 50f;    // one plotted point per 50 ms
    private const float WindowSeconds    = Capacity * SampleIntervalMs / 1000f;
    private const int   Divs             = 5;

    private static readonly string WindowLabel = $"-{WindowSeconds:0}s";

    private readonly float[] _samples = new float[Capacity];
    private readonly Vector2[] _polyScratch = new Vector2[Capacity];
    
    private int _head;
    private int _count;
    
    private float _accMs;
    private float _accFps;
    private int   _accN;
    
    private float _yMax = 60f;
    private readonly string[] _tickLabels = new string[Divs + 1];
    private string _currentLabel = "0";

    public PerformanceGraph() => RefreshLabels();

    public void Push(float fps, float frameTimeMs)
    {
        _accFps += fps;
        _accN++;
        _accMs += frameTimeMs;
        if (_accMs < SampleIntervalMs) return;

        _samples[_head] = _accN > 0 ? _accFps / _accN : fps;
        _head = (_head + 1) % Capacity;
        
        if (_count < Capacity) 
            _count++;

        _accMs = 0f;
        _accFps = 0f;
        _accN = 0;
        
        RefreshLabels();
    }
    
    private void RefreshLabels()
    {
        var peak = 1f;
        for (var i = 0; i < _count; i++) 
            peak = MathF.Max(peak, _samples[i]);
        _yMax = NiceCeil(peak);

        for (var d = 0; d <= Divs; d++)
            _tickLabels[d] = FormatTick(_yMax * d / (float)Divs);

        var current = _samples[(_head - 1 + Capacity) % Capacity];
        _currentLabel = $"{current:0} FPS";
    }

    public void Draw()
    {
        var graphHeight = Widgets.Scale(GraphHeight);
        var leftPad     = Widgets.Scale(LeftPad);
        var botPad      = Widgets.Scale(BotPad);
        var topPad      = Widgets.Scale(TopPad);

        var avail  = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(avail, graphHeight));    // reserve layout space

        var p0 = new Vector2(origin.X + leftPad, origin.Y + topPad);
        var p1 = new Vector2(origin.X + avail,   origin.Y + graphHeight - botPad);
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
        var yMax = _yMax;

        for (var d = 0; d <= Divs; d++)
        {
            var t = d / (float)Divs;
            var y = p1.Y - t * h;
            dl.AddLine(new Vector2(p0.X, y), new Vector2(p1.X, y), grid);

            var label = _tickLabels[d];
            var sz = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(p0.X - Widgets.Scale(6f) - sz.X, y - sz.Y * 0.5f), tick, label);
        }

        // ── series ────────────────────────────────────────────────────────────
        // Single AddPolyline call instead of (count-1) AddLine calls — same geometry, far
        // fewer draw-list submissions (this graph redraws every frame while its underlying
        // samples only change once per SampleIntervalMs).
        if (_count >= 2)
        {
            for (var i = 0; i < _count; i++)
            {
                var idx = (_head - _count + i + Capacity) % Capacity;  // chronological
                var x = p0.X + w * (i / (float)(_count - 1));
                var y = p1.Y - h * MathF.Min(_samples[idx] / yMax, 1f);
                
                _polyScratch[i] = new Vector2(x, y);
            }
            dl.AddPolyline(ref _polyScratch[0], _count, line, ImDrawFlags.None, 1.5f);
        }

        // ── X labels + current value ───────────────────────────────────────────
        var labelGap = Widgets.Scale(2f);
        dl.AddText(new Vector2(p0.X, p1.Y + labelGap), tick, WindowLabel);
        var nowSz = ImGui.CalcTextSize("now");
        dl.AddText(new Vector2(p1.X - nowSz.X, p1.Y + labelGap), tick, "now");

        if (_count > 0)
        {
            var sz = ImGui.CalcTextSize(_currentLabel);
            dl.AddText(new Vector2(p1.X - sz.X - Widgets.Scale(4f), p0.Y + labelGap), line, _currentLabel);
        }
    }

    // Round up to a tidy axis maximum (… 1, 2, 2.5, 5 × 10ⁿ …), min 60.
    private static float NiceCeil(float v)
    {
        if (v <= 60f) 
            return 60f;
        
        var mag  = MathF.Pow(10f, MathF.Floor(MathF.Log10(v)));
        var n    = v / mag;
        var nice = n <= 1f ? 1f : n <= 2f ? 2f : n <= 2.5f ? 2.5f : n <= 5f ? 5f : 10f;
        return nice * mag;
    }

    private static string FormatTick(float v) => v >= 1000f ? $"{v / 1000f:0.#}k" : $"{v:0}";
}