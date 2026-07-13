namespace Centauri.UI.Common;

using ImGuiNET;
using System.Numerics;
using System.Globalization;

internal static class Widgets
{
    // Every fixed-pixel layout constant across the UI (label widths, graph padding, panel
    // sizes, Theme's style metrics) is tuned against this design-time font size — matching
    // config.json's default imGui.fontSize. Scale() converts a baseline value to whatever font
    // size is actually active, so panels stay proportioned instead of overlapping/clipping when
    // fontSize changes (see GPUTimingGraph's header-alignment crash for what skipping this
    // leads to). Set once at startup (ImGuiManager, alongside loading the font) rather than
    // derived from ImGui.GetFontSize() per call: several panels compute window position/size
    // before pushing the UI font each frame, where GetFontSize() would still read whatever
    // font was last active (the default font, before the first PushFont) instead of the real one.
    public const  float DesignFontSize = 18f;
    public static float FontScale { get; private set; } = 1f;
    public static void  SetFontScale(float fontSize) => FontScale = fontSize / DesignFontSize;
    public static float Scale(float px) => px * FontScale;

    private const float LabelFraction = 0.42f;
    private const float LabelGap      = 8f;
    private const float PanelIndent   = 8f;
    
    public const ImGuiWindowFlags PanelBase = ImGuiWindowFlags.NoMove          |
                                              ImGuiWindowFlags.NoCollapse      |
                                              ImGuiWindowFlags.NoSavedSettings |
                                              ImGuiWindowFlags.AlwaysAutoResize;
    
    private const ImGuiColorEditFlags SwatchFlags = ImGuiColorEditFlags.NoInputs;
    
    public static Vector4 BooleanColor(bool value) => value ? ColorPalette.Green : ColorPalette.Red;

    private static bool BeginPanel(string label, bool defaultOpen = true)
        => BeginPanel(label, Vector4.Zero, defaultOpen);

    public static bool BeginPanel(string label, Vector4 accent, bool defaultOpen = true)
    {
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        var tinted = accent.W > 0f;
        if (tinted) 
            ImGui.PushStyleColor(ImGuiCol.Text, accent);
        
        var open = ImGui.CollapsingHeader(label, flags);
        if (tinted) 
            ImGui.PopStyleColor();

        if (open)
            ImGui.Indent(Scale(PanelIndent));
        return open;
    }

    public static void EndPanel(bool open)
    {
        if (open)
            ImGui.Unindent(Scale(PanelIndent));
        ImGui.Spacing();
    }
    
    public readonly ref struct PanelScope
    {
        public bool Open { get; }

        public PanelScope(string label, bool startCollapsed)
        {
            if (startCollapsed)
                ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);

            Open = BeginPanel(label);
            if (Open) ImGui.PushID(label);
        }

        public void Dispose()
        {
            if (Open) ImGui.PopID();
            EndPanel(Open);
        }
    }

    public static PanelScope Section(string label, bool startCollapsed = false) => new(label, startCollapsed);

    // Lays out a right-aligned label and sizes the next item to fill the row.
    private static void RowLabel(string label)
    {
        var avail  = ImGui.GetContentRegionAvail().X;
        var labelW = avail * LabelFraction;
        var startX = ImGui.GetCursorPosX();
        var textW  = ImGui.CalcTextSize(label).X;

        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosX(startX + MathF.Max(0f, labelW - textW - Scale(LabelGap)));
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.SetCursorPosX(startX + labelW);
        ImGui.SetNextItemWidth(MathF.Max(1f, avail - labelW));
    }

    // Three axis rows. `reset` is the per-axis default (right-click to apply);
    // `active` is true while any axis is being dragged/typed.
    public static bool Vec3Rows(string label, ref Vector3 v, float speed, string fmt, Vector3 reset, out bool active)
    {
        active = false;
        var changed = false;
        
        changed |= AxisRow($"{label} X", "##" + label + "X", ref v.X, speed, fmt, reset.X, ref active);
        changed |= AxisRow("Y",          "##" + label + "Y", ref v.Y, speed, fmt, reset.Y, ref active);
        changed |= AxisRow("Z",          "##" + label + "Z", ref v.Z, speed, fmt, reset.Z, ref active);
        return changed;
    }

    // Write-back-via-setter convenience (for vectors where stale-caching isn't a concern).
    public static void Vec3Rows(string label, Vector3 v, Action<Vector3> set, float speed, string fmt, Vector3 reset)
    {
        if (Vec3Rows(label, ref v, speed, fmt, reset, out _)) 
            set(v);
    }

    private static bool AxisRow(string label, string id, ref float v, float speed, string fmt, float reset, ref bool active)
    {
        RowLabel(label);
        var changed = ImGui.DragFloat(id, ref v, speed, 0f, 0f, fmt);
        
        active |= ImGui.IsItemActive();
        changed |= ResetMenu(id, ref v, reset);
        return changed;
    }

    // Single packed row (ImGui.DragFloat2), unlike Vec3Rows' three separate axis lines — right
    // for a pair that's always edited together and doesn't need per-axis reset (UV scale/offset
    // have no "Authored" baseline to reset to, unlike Transform).
    public static void Vec2Row(string label, Vector2 v, Action<Vector2> set, float speed, string fmt = "%.3f")
    {
        RowLabel(label);

        var id = "##" + label;
        if (ImGui.DragFloat2(id, ref v, speed, 0f, 0f, fmt))
            set(v);
    }

    public static void DragRow(string label, float v, Action<float> set, float speed, float min, float max, string fmt = "%.3f", float? reset = null)
    {
        RowLabel(label);
        
        var id = "##" + label;
        var changed = ImGui.DragFloat(id, ref v, speed, min, max, fmt);
        
        if (reset is { } r) 
            changed |= ResetMenu(id, ref v, r);
        
        if (changed) 
            set(v);
    }

    public static void SliderRow(string label, float v, Action<float> set, float min, float max, float? reset = null)
    {
        RowLabel(label);
        
        var id = "##" + label;
        var changed = ImGui.SliderFloat(id, ref v, min, max, "%.3f");
        
        if (reset is { } r) 
            changed |= ResetMenu(id, ref v, r);
        
        if (changed) 
            set(v);
    }

    public static void ColorRow4(string label, Vector4 v, Action<Vector4> set)
    {
        RowLabel(label);
        if (ImGui.ColorEdit4("##" + label, ref v, SwatchFlags)) 
            set(v);
    }

    public static void ColorRow3(string label, Vector3 v, Action<Vector3> set)
    {
        RowLabel(label);
        if (ImGui.ColorEdit3("##" + label, ref v, SwatchFlags)) 
            set(v);
    }

    public static void CheckRow(string label, bool v, Action<bool> set)
    {
        RowLabel(label);
        if (ImGui.Checkbox("##" + label, ref v)) 
            set(v);
    }

    public static bool ComboRow(string label, ref int index, string[] items)
    {
        RowLabel(label);
        return ImGui.Combo("##" + label, ref index, items, items.Length);
    }
    
    private static bool ResetMenu(string id, ref float v, float reset)
    {
        if (!ImGui.BeginPopupContextItem(id)) 
            return false;
        
        var hit = ImGui.MenuItem("Reset");
        if (hit) 
            v = reset;
        
        ImGui.EndPopup();
        
        return hit;
    }

    // ── formatting ──────────────────────────────────────────────────────────────
    public static string Vec3(Vector3 v) => string.Format(
        CultureInfo.CurrentCulture,
        "({0,8:+0.00;-0.00}, {1,8:+0.00;-0.00}, {2,8:+0.00;-0.00})",
        v.X, v.Y, v.Z);

    public static string Float(float v, int decimals = 2) =>
        v.ToString($"F{decimals}",
            CultureInfo.CurrentCulture);

    public static string SignedFloat(float v, int decimals = 2) =>
        v.ToString($"+0.{new string('0', decimals)};-0.{new string('0', decimals)}",
            CultureInfo.CurrentCulture);
}