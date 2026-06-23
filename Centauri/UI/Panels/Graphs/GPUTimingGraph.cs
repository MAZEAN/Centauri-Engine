namespace Centauri.UI.Panels;

using ImGuiNET;
using System.Numerics;

using Common;
using Rendering.Profiling;

// Rolling per-pass GPU time as a stacked area graph: one colored band per pass, summed to
// the frame's GPU total. Like PerformanceGraph, samples are interval-averaged so the plot
// stays steady regardless of frame rate, and the Y axis auto-scales to a tidy ms ceiling.
// A legend below shows each pass's latest (interval-averaged) cost — calm enough to read,
// unlike the per-frame text it replaces.
internal sealed class GpuTimingGraph
{
    private const float GraphHeight = 150f;
    private const float LeftPad = 36f;   // Y tick-label gutter
    private const float BotPad  = 20f;   // X label gutter
    private const float TopPad  = 20f;

    private const int   Capacity         = 200;    // samples retained
    private const float SampleIntervalMs = 50f;    // one plotted point per 50 ms
    private const float WindowSeconds    = Capacity * SampleIntervalMs / 1000f;
    private const int   MaxZones         = 8;

    private static readonly Vector4[] Palette =
    [
        ColorPalette.Amber, ColorPalette.Blue,  ColorPalette.Green, ColorPalette.Red,
        ColorPalette.Purple, new(0.40f, 0.85f, 0.85f, 1f), new(0.85f, 0.55f, 0.30f, 1f),
        new(0.65f, 0.65f, 0.65f, 1f),
    ];

    private readonly float[,] _samples = new float[Capacity, MaxZones];   // per-interval, per-zone ms
    private readonly string[] _names   = new string[MaxZones];
    private int _zoneCount;
    private int _head;     // next write slot
    private int _count;    // valid samples (≤ Capacity)

    // accumulator for interval averaging
    private readonly float[] _acc = new float[MaxZones];
    private float _accMs;
    private int   _accN;

    public void Push(IReadOnlyList<GpuTiming> timings, float frameTimeMs)
    {
        for (var i = 0; i < timings.Count; i++)
        {
            var slot = Slot(timings[i].Name);
            if (slot >= 0) _acc[slot] += (float)timings[i].Milliseconds;
        }

        _accN++;
        _accMs += frameTimeMs;
        if (_accMs < SampleIntervalMs) return;

        for (var z = 0; z < _zoneCount; z++)
        {
            _samples[_head, z] = _accN > 0 ? _acc[z] / _accN : 0f;
            _acc[z] = 0f;
        }

        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
        _accMs = 0f;
        _accN  = 0;
    }

    private int Slot(string name)
    {
        for (var z = 0; z < _zoneCount; z++)
            if (_names[z] == name) return z;

        if (_zoneCount >= MaxZones) return -1;
        _names[_zoneCount] = name;
        return _zoneCount++;
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

        dl.AddRectFilled(p0, p1, bg);
        dl.AddRect(p0, p1, edge);

        // ── Y scale from the largest stacked total ──────────────────────────────
        var peak = 0.01f;
        for (var i = 0; i < _count; i++)
        {
            var sum = 0f;
            for (var z = 0; z < _zoneCount; z++) sum += _samples[i, z];
            peak = MathF.Max(peak, sum);
        }
        var yMax = NiceCeil(peak);

        const int divs = 5;
        for (var d = 0; d <= divs; d++)
        {
            var t = d / (float)divs;
            var y = p1.Y - t * h;
            dl.AddLine(new Vector2(p0.X, y), new Vector2(p1.X, y), grid);

            var label = $"{yMax * t:0.0}";
            var sz = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(p0.X - 6f - sz.X, y - sz.Y * 0.5f), tick, label);
        }

        // ── stacked bands ───────────────────────────────────────────────────────
        if (_count >= 2)
        {
            for (var i = 0; i < _count - 1; i++)
            {
                var ia = (_head - _count + i     + Capacity) % Capacity;   // chronological
                var ib = (_head - _count + i + 1 + Capacity) % Capacity;
                var xa = p0.X + w * (i       / (float)(_count - 1));
                var xb = p0.X + w * ((i + 1) / (float)(_count - 1));

                float baseA = 0f, baseB = 0f;
                for (var z = 0; z < _zoneCount; z++)
                {
                    var topA = baseA + _samples[ia, z];
                    var topB = baseB + _samples[ib, z];

                    var c   = Palette[z % Palette.Length];
                    var col = ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, 0.65f));

                    dl.AddQuadFilled(
                        new Vector2(xa, Y(p1.Y, h, baseA, yMax)),
                        new Vector2(xa, Y(p1.Y, h, topA,  yMax)),
                        new Vector2(xb, Y(p1.Y, h, topB,  yMax)),
                        new Vector2(xb, Y(p1.Y, h, baseB, yMax)),
                        col);

                    baseA = topA;
                    baseB = topB;
                }
            }
        }

        // ── X labels ────────────────────────────────────────────────────────────
        dl.AddText(new Vector2(p0.X, p1.Y + 2f), tick, $"-{WindowSeconds:0}s");
        var nowSz = ImGui.CalcTextSize("now");
        dl.AddText(new Vector2(p1.X - nowSz.X, p1.Y + 2f), tick, "now");

        DrawLegend();
    }

    private void DrawLegend()
    {
        var last  = (_head - 1 + Capacity) % Capacity;
        var total = 0f;

        for (var z = 0; z < _zoneCount; z++)
        {
            var ms = _count > 0 ? _samples[last, z] : 0f;
            total += ms;

            ImGui.ColorButton($"##gpu{z}", Palette[z % Palette.Length],
                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder, new Vector2(10f, 10f));
            ImGui.SameLine();
            ImGui.TextUnformatted($"{_names[z]}: {ms:0.000} ms");
        }

        ImGui.Separator();
        ImGui.TextUnformatted($"Total: {total:0.000} ms");
    }

    private static float Y(float baseY, float h, float value, float yMax) =>
        baseY - h * MathF.Min(value / yMax, 1f);

    // Round up to a tidy axis maximum (… 1, 2, 2.5, 5 × 10ⁿ …), min 1 ms.
    private static float NiceCeil(float v)
    {
        if (v <= 1f) return 1f;

        var mag  = MathF.Pow(10f, MathF.Floor(MathF.Log10(v)));
        var n    = v / mag;
        var nice = n <= 1f ? 1f : n <= 2f ? 2f : n <= 2.5f ? 2.5f : n <= 5f ? 5f : 10f;
        return nice * mag;
    }
}