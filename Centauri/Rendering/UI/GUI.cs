namespace Centauri.Rendering.UI;

using ImGuiNET;
using System.Numerics;
using System.Globalization;

// Shared visual language + widget helpers for the engine's ImGui panels.
internal static class GUI
{
    // ── palette ────────────────────────────────────────────────────────────────
    public static readonly Vector4 Amber   = new(1.00f, 0.75f, 0.20f, 1f);
    public static readonly Vector4 Green   = new(0.45f, 0.90f, 0.45f, 1f);
    public static readonly Vector4 Blue    = new(0.40f, 0.70f, 1.00f, 1f);
    public static readonly Vector4 Red     = new(1.00f, 0.35f, 0.35f, 1f);
    public static readonly Vector4 Purple  = new(0.70f, 0.50f, 1.00f, 1f);
    public static readonly Vector4 White   = Vector4.One;

    public static Vector4 Bool(bool value) => value ? Green : Red;

    // ── section header (identical across panels) ────────────────────────────────
    public static void SectionTitle(string title, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, accent);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.Separator();
    }
    
        // ── Blender-style property panels & rows ────────────────────────────────────
    // A panel is a full-width collapsing header (with disclosure triangle); its
    // body is indented, and each control sits on its own row with a right-aligned
    // label in a fixed left column and the field filling the remainder.
    private const float LabelFraction = 0.42f; // share of the row taken by the label
    private const float LabelGap      = 8f;    // gap between label and field
    private const float PanelIndent   = 8f;

    public static bool BeginPanel(string label, bool defaultOpen = true)
        => BeginPanel(label, Vector4.Zero, defaultOpen);

    public static bool BeginPanel(string label, Vector4 accent, bool defaultOpen = true)
    {
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        var tinted = accent.W > 0f;                       // colored header title
        if (tinted) ImGui.PushStyleColor(ImGuiCol.Text, accent);
        
        var open = ImGui.CollapsingHeader(label, flags);
        if (tinted) ImGui.PopStyleColor();

        if (open) ImGui.Indent(PanelIndent);
        return open;
    }

    public static void EndPanel(bool open)
    {
        if (open) ImGui.Unindent(PanelIndent);
        ImGui.Spacing();
    }

    // Lays out a right-aligned label and sizes the next item to fill the row.
    private static void RowLabel(string label)
    {
        var avail  = ImGui.GetContentRegionAvail().X;
        var labelW = avail * LabelFraction;
        var startX = ImGui.GetCursorPosX();
        var textW  = ImGui.CalcTextSize(label).X;

        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosX(startX + MathF.Max(0f, labelW - textW - LabelGap));
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGui.SetCursorPosX(startX + labelW);
        ImGui.SetNextItemWidth(MathF.Max(1f, avail - labelW));
    }
    
    // Read-only "label : value" row (for HUD/stat panels).
    public static void TextRow(string label, string value) => TextRow(label, value, Vector4.Zero);

    public static void TextRow(string label, string value, Vector4 color)
    {
        RowLabel(label);                       // right-aligned label, advances to value column
        ImGui.AlignTextToFramePadding();
        bool tinted = color.W > 0f;
        if (tinted) ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(value);
        if (tinted) ImGui.PopStyleColor();
    }

    // Three stacked axis rows (Blender shows the property name only on the first).
    public static bool Vec3Rows(string label, ref Vector3 v, float speed, string fmt)
    {
        var changed = false;
        changed |= AxisRow($"{label} X", "##" + label + "X", ref v.X, speed, fmt);
        changed |= AxisRow("Y",          "##" + label + "Y", ref v.Y, speed, fmt);
        changed |= AxisRow("Z",          "##" + label + "Z", ref v.Z, speed, fmt);
        return changed;
    }

    public static void Vec3Rows(string label, Vector3 v, Action<Vector3> set, float speed, string fmt)
    {
        if (Vec3Rows(label, ref v, speed, fmt)) set(v);
    }

    private static bool AxisRow(string label, string id, ref float v, float speed, string fmt)
    {
        RowLabel(label);
        return ImGui.DragFloat(id, ref v, speed, 0f, 0f, fmt);
    }

    public static void DragRow(string label, float v, Action<float> set,
                               float speed, float min, float max, string fmt = "%.3f")
    {
        RowLabel(label);
        if (ImGui.DragFloat("##" + label, ref v, speed, min, max, fmt)) set(v);
    }

    public static void SliderRow(string label, float v, Action<float> set, float min, float max)
    {
        RowLabel(label);
        if (ImGui.SliderFloat("##" + label, ref v, min, max, "%.3f")) set(v);
    }

    public static void ColorRow4(string label, Vector4 v, Action<Vector4> set)
    {
        RowLabel(label);
        if (ImGui.ColorEdit4("##" + label, ref v, SwatchFlags)) set(v);
    }

    public static void ColorRow3(string label, Vector3 v, Action<Vector3> set)
    {
        RowLabel(label);
        if (ImGui.ColorEdit3("##" + label, ref v, SwatchFlags)) set(v);
    }

    public static void CheckRow(string label, bool v, Action<bool> set)
    {
        RowLabel(label);
        if (ImGui.Checkbox("##" + label, ref v)) set(v);
    }

    public static bool ComboRow(string label, ref int index, string[] items)
    {
        RowLabel(label);
        return ImGui.Combo("##" + label, ref index, items, items.Length);
    }

    private const ImGuiColorEditFlags SwatchFlags = ImGuiColorEditFlags.NoInputs;

    // ── formatting ──────────────────────────────────────────────────────────────
    public static string Vec3(Vector3 v) => string.Format(
        CultureInfo.CurrentCulture,
        "({0,8:+0.00;-0.00}, {1,8:+0.00;-0.00}, {2,8:+0.00;-0.00})", v.X, v.Y, v.Z);

    public static string Float(float v, int decimals = 2) =>
        v.ToString($"F{decimals}", CultureInfo.CurrentCulture);

    public static string SignedFloat(float v, int decimals = 2) =>
        v.ToString($"+0.{new string('0', decimals)};-0.{new string('0', decimals)}", CultureInfo.CurrentCulture);
}