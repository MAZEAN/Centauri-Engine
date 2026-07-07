namespace Centauri.UI.Panels.Graphs;

using ImGuiNET;
using System.Numerics;

using Common;
using Rendering.Profiling;

// Rolling per-pass GPU time as a stacked area graph: one colored band per pass, summed to
// the frame's GPU total. Like PerformanceGraph, samples are interval-averaged so the plot
// stays steady regardless of frame rate, and the Y axis auto-scales to a tidy ms ceiling.
// A legend below shows each pass's latest (interval-averaged) cost — calm enough to read,
// unlike the per-frame text it replaces.
internal sealed class GPUTimingGraph
{
    private const float GraphHeight = 150f;
    private const float LeftPad = 36f;   // Y tick-label gutter
    private const float BotPad  = 20f;   // X label gutter
    private const float TopPad  = 20f;
    private const float IconSize = 10f;

    private const int   Capacity         = 200;    // samples retained
    private const float SampleIntervalMs = 50f;    // one plotted point per 50 ms
    private const float WindowSeconds    = Capacity * SampleIntervalMs / 1000f;
    private const int   MaxZones         = 8;
    private const int   WarmupFrames     = 100;
    private const int   Divs = 5;

    private static readonly string WindowLabel = $"-{WindowSeconds:0}s";
    
    private enum HeaderAlign { Left, Right }

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
    private int _framesSeen;
    
    private float _yMax = 1f;
    private readonly string[] _tickLabels = new string[Divs + 1];
    private readonly string[] _msLabels  = CreateFilledArray(MaxZones, "0.000");
    private readonly string[] _pctLabels = CreateFilledArray(MaxZones, "0.0%");
    private string _totalLabel = "0.000";

    public GPUTimingGraph() => RefreshLabels();

    public void Push(IReadOnlyList<GpuTiming> timings, float frameTimeMs)
    {
        if (_framesSeen < WarmupFrames)
        {
            _framesSeen++;
            return;
        }
        
        for (var i = 0; i < timings.Count; i++)
        {
            var slot = Slot(timings[i].Name);
            if (slot >= 0) 
                _acc[slot] += (float)timings[i].Milliseconds;
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
        if (_count < Capacity) 
            _count++;
        
        _accMs = 0f;
        _accN  = 0;
        
        RefreshLabels();
    }
    
    private void RefreshLabels()
    {
        var peak = 0.01f;
        for (var i = 0; i < _count; i++)
        {
            var sum = 0f;
            for (var z = 0; z < _zoneCount; z++)
                sum += _samples[i, z];
            peak = MathF.Max(peak, sum);
        }
        _yMax = NiceCeil(peak);

        for (var d = 0; d <= Divs; d++)
            _tickLabels[d] = $"{_yMax * d / (float)Divs:0.0}";

        var last  = (_head - 1 + Capacity) % Capacity;
        var total = 0f;
        for (var z = 0; z < _zoneCount; z++)
        {
            var ms = _count > 0 ? _samples[last, z] : 0f;
            total += ms;
        }

        for (var z = 0; z < _zoneCount; z++)
        {
            var ms = _count > 0 ? _samples[last, z] : 0f;
            var percentage = total > 0.00001f ? ms / total * 100f : 0f;
            _msLabels[z]  = $"{ms:0.000}";
            _pctLabels[z] = $"{percentage:0.0}%";
        }
        _totalLabel = $"{total:0.000}";
    }

    private int Slot(string name)
    {
        for (var z = 0; z < _zoneCount; z++)
            if (_names[z] == name) 
                return z;

        if (_zoneCount >= MaxZones) 
            return -1;
        
        _names[_zoneCount] = name;
        return _zoneCount++;
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

        dl.AddRectFilled(p0, p1, bg);
        dl.AddRect(p0, p1, edge);

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

        
        var labelGap = Widgets.Scale(2f);
        dl.AddText(new Vector2(p0.X, p1.Y + labelGap), tick, WindowLabel);
        var nowSz = ImGui.CalcTextSize("now");
        dl.AddText(new Vector2(p1.X - nowSz.X, p1.Y + labelGap), tick, "now");

        DrawLegend();
    }

    private void DrawLegend()
    {
        if (!BeginLegendTable()) return;

        for (var z = 0; z < _zoneCount; z++)
            DrawLegendRow(z);

        DrawTotalRow();

        ImGui.EndTable();
    }
    
    private static bool BeginLegendTable()
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.NoHostExtendX;

        if (!ImGui.BeginTable("GpuLegend", 3, flags))
            return false;

        ImGui.TableSetupColumn("Pass", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Time (ms)", ImGuiTableColumnFlags.WidthFixed, Widgets.Scale(90f));
        ImGui.TableSetupColumn("%", ImGuiTableColumnFlags.WidthFixed, Widgets.Scale(70f));

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        DrawHeaderCell(0, "Pass", HeaderAlign.Left);
        DrawHeaderCell(1, "Time (ms)", HeaderAlign.Right);
        DrawHeaderCell(2, "%", HeaderAlign.Right);

        return true;
    }

    private static void DrawHeaderCell(int column, string text, HeaderAlign align = HeaderAlign.Right)
    {
        ImGui.TableSetColumnIndex(column);

        var width = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;

        switch (align)
        {
            case HeaderAlign.Right:
                if (avail > width)
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - width);
                break;
            case HeaderAlign.Left:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(align), align, null);
        }

        ImGui.TextUnformatted(text);
    }
    
    private void DrawLegendRow(int zone)
    {
        ImGui.TableNextRow();

        DrawPassCell(zone);
        DrawRightAlignedCell(1, _msLabels[zone]);
        DrawRightAlignedCell(2, _pctLabels[zone]);
    }
    
    private void DrawTotalRow()
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled("Total");

        DrawRightAlignedCell(1, _totalLabel);
        DrawRightAlignedCell(2, "100.0%");
    }
    
    private void DrawPassCell(int zone)
    {
        ImGui.TableSetColumnIndex(0);

        var iconSize = Widgets.Scale(IconSize);

        // vertical centering inside row
        var yOffset = iconSize * 0.5f;
        if (yOffset > 0f)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + yOffset);

        ImGui.ColorButton(
            $"##gpu{zone}",
            Palette[zone % Palette.Length],
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder,
            new Vector2(iconSize, iconSize));

        ImGui.SameLine(0f, Widgets.Scale(6f));

        // reset Y so text aligns with row baseline nicely
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - yOffset);

        ImGui.TextUnformatted(_names[zone]);
    }
    
    private static void DrawRightAlignedCell(int column, string text)
    {
        ImGui.TableSetColumnIndex(column);

        var width = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;

        if (avail > width)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - width);

        ImGui.TextUnformatted(text);
    }

    private static float Y(float baseY, float h, float value, float yMax) =>
        baseY - h * MathF.Min(value / yMax, 1f);

    private static float NiceCeil(float v)
    {
        if (v <= 1f) return 1f;

        var mag  = MathF.Pow(10f, MathF.Floor(MathF.Log10(v)));
        var n    = v / mag;
        var nice = n <= 1f ? 1f : n <= 2f ? 2f : n <= 2.5f ? 2.5f : n <= 5f ? 5f : 10f;
        return nice * mag;
    }
    
    private static string[] CreateFilledArray(int length, string value)
    {
        var array = new string[length];
        Array.Fill(array, value);
        return array;
    }
}