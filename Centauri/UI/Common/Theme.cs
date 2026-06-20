namespace Centauri.UI.Common;

using ImGuiNET;
using System.Numerics;

// Global ImGui style tuned to resemble Blender's dark "Properties" editor.
// Applied once, after the ImGui context exists, so every panel inherits it.
internal static class Theme
{
    public static void ApplyBlenderDark()
    {
        var style = ImGui.GetStyle();

        // ── metrics ─────────────────────────────────────────────────────────────
        style.WindowRounding    = 4f;
        style.ChildRounding     = 4f;
        style.FrameRounding     = 4f;
        style.PopupRounding     = 4f;
        style.GrabRounding      = 4f;
        style.TabRounding       = 4f;
        style.ScrollbarRounding = 8f;

        style.WindowPadding    = new Vector2(8f, 8f);
        style.FramePadding     = new Vector2(8f, 4f);
        style.CellPadding      = new Vector2(6f, 3f);
        style.ItemSpacing      = new Vector2(6f, 4f);
        style.ItemInnerSpacing = new Vector2(6f, 4f);
        style.IndentSpacing    = 14f;
        style.ScrollbarSize    = 12f;
        style.GrabMinSize      = 9f;

        style.WindowBorderSize = 0f;
        style.FrameBorderSize  = 0f;
        style.PopupBorderSize  = 1f;

        style.WindowTitleAlign    = new Vector2(0.0f, 0.5f);
        style.ColorButtonPosition = ImGuiDir.Left;

        var colorStyle = style.Colors;

        colorStyle[(int)ImGuiCol.Text]             = ColorPalette.Text;
        colorStyle[(int)ImGuiCol.TextDisabled]     = ColorPalette.TextDim;
        colorStyle[(int)ImGuiCol.WindowBg]         = ColorPalette.WindowBg;
        colorStyle[(int)ImGuiCol.ChildBg]          = ColorPalette.PanelBg;
        colorStyle[(int)ImGuiCol.PopupBg]          = new Vector4(0.13f, 0.13f, 0.13f, 0.98f);
        colorStyle[(int)ImGuiCol.Border]           = ColorPalette.Line;
        colorStyle[(int)ImGuiCol.BorderShadow]     = new Vector4(0f, 0f, 0f, 0f);

        colorStyle[(int)ImGuiCol.FrameBg]          = ColorPalette.Field;
        colorStyle[(int)ImGuiCol.FrameBgHovered]   = ColorPalette.FieldHi;
        colorStyle[(int)ImGuiCol.FrameBgActive]    = ColorPalette.AccentDim;

        colorStyle[(int)ImGuiCol.TitleBg]          = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        colorStyle[(int)ImGuiCol.TitleBgActive]    = new Vector4(0.14f, 0.14f, 0.14f, 1.00f);
        colorStyle[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);

        colorStyle[(int)ImGuiCol.Header]           = ColorPalette.Header;
        colorStyle[(int)ImGuiCol.HeaderHovered]    = ColorPalette.HeaderHi;
        colorStyle[(int)ImGuiCol.HeaderActive]     = ColorPalette.Header;

        colorStyle[(int)ImGuiCol.Button]           = ColorPalette.Field;
        colorStyle[(int)ImGuiCol.ButtonHovered]    = ColorPalette.FieldHi;
        colorStyle[(int)ImGuiCol.ButtonActive]     = ColorPalette.Accent;

        colorStyle[(int)ImGuiCol.SliderGrab]       = new Vector4(0.55f, 0.55f, 0.55f, 1.00f);
        colorStyle[(int)ImGuiCol.SliderGrabActive] = ColorPalette.Accent;
        colorStyle[(int)ImGuiCol.CheckMark]        = new Vector4(0.92f, 0.92f, 0.92f, 1.00f);

        colorStyle[(int)ImGuiCol.Separator]        = ColorPalette.Line;
        colorStyle[(int)ImGuiCol.SeparatorHovered] = ColorPalette.AccentDim;
        colorStyle[(int)ImGuiCol.SeparatorActive]  = ColorPalette.Accent;
        
        colorStyle[(int)ImGuiCol.Tab]             = ColorPalette.Header;
        colorStyle[(int)ImGuiCol.TabHovered]      = ColorPalette.HeaderHi;
        colorStyle[(int)ImGuiCol.TabActive]       = ColorPalette.AccentDim;

        colorStyle[(int)ImGuiCol.ScrollbarBg]          = new Vector4(0.13f, 0.13f, 0.13f, 1.00f);
        colorStyle[(int)ImGuiCol.ScrollbarGrab]        = new Vector4(0.32f, 0.32f, 0.32f, 1.00f);
        colorStyle[(int)ImGuiCol.ScrollbarGrabHovered] = ColorPalette.FieldHi;
        colorStyle[(int)ImGuiCol.ScrollbarGrabActive]  = new Vector4(0.42f, 0.42f, 0.42f, 1.00f);

        colorStyle[(int)ImGuiCol.TextSelectedBg]   = ColorPalette.AccentDim;
        colorStyle[(int)ImGuiCol.NavHighlight]     = ColorPalette.Accent;
        colorStyle[(int)ImGuiCol.DragDropTarget]   = ColorPalette.Accent;
    }
}