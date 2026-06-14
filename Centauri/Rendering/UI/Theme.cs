namespace Centauri.Rendering.UI;

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
        style.FrameRounding     = 4f;   // Blender's number fields are gently rounded
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

        // ── palette ─────────────────────────────────────────────────────────────
        var text      = new Vector4(0.90f, 0.90f, 0.90f, 1.00f);
        var textDim   = new Vector4(0.55f, 0.55f, 0.55f, 1.00f);
        var windowBg  = new Vector4(0.17f, 0.17f, 0.17f, 1.00f);
        var panelBg   = new Vector4(0.21f, 0.21f, 0.21f, 1.00f);
        var field     = new Vector4(0.28f, 0.28f, 0.28f, 1.00f);
        var fieldHi   = new Vector4(0.33f, 0.33f, 0.33f, 1.00f);
        var header    = new Vector4(0.30f, 0.30f, 0.30f, 1.00f);
        var headerHi  = new Vector4(0.36f, 0.36f, 0.36f, 1.00f);
        var accent    = new Vector4(0.22f, 0.46f, 0.80f, 1.00f);   // Blender selection blue
        var accentDim = new Vector4(0.20f, 0.39f, 0.66f, 1.00f);
        var line      = new Vector4(0.10f, 0.10f, 0.10f, 1.00f);

        var c = style.Colors;

        c[(int)ImGuiCol.Text]             = text;
        c[(int)ImGuiCol.TextDisabled]     = textDim;
        c[(int)ImGuiCol.WindowBg]         = windowBg;
        c[(int)ImGuiCol.ChildBg]          = panelBg;
        c[(int)ImGuiCol.PopupBg]          = new Vector4(0.13f, 0.13f, 0.13f, 0.98f);
        c[(int)ImGuiCol.Border]           = line;
        c[(int)ImGuiCol.BorderShadow]     = new Vector4(0f, 0f, 0f, 0f);

        c[(int)ImGuiCol.FrameBg]          = field;
        c[(int)ImGuiCol.FrameBgHovered]   = fieldHi;
        c[(int)ImGuiCol.FrameBgActive]    = accentDim;

        c[(int)ImGuiCol.TitleBg]          = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
        c[(int)ImGuiCol.TitleBgActive]    = new Vector4(0.14f, 0.14f, 0.14f, 1.00f);
        c[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);

        c[(int)ImGuiCol.Header]           = header;     // collapsing-header (panel) row
        c[(int)ImGuiCol.HeaderHovered]    = headerHi;
        c[(int)ImGuiCol.HeaderActive]     = header;

        c[(int)ImGuiCol.Button]           = field;
        c[(int)ImGuiCol.ButtonHovered]    = fieldHi;
        c[(int)ImGuiCol.ButtonActive]     = accent;

        c[(int)ImGuiCol.SliderGrab]       = new Vector4(0.55f, 0.55f, 0.55f, 1.00f);
        c[(int)ImGuiCol.SliderGrabActive] = accent;
        c[(int)ImGuiCol.CheckMark]        = new Vector4(0.92f, 0.92f, 0.92f, 1.00f);

        c[(int)ImGuiCol.Separator]        = line;
        c[(int)ImGuiCol.SeparatorHovered] = accentDim;
        c[(int)ImGuiCol.SeparatorActive]  = accent;
        
        c[(int)ImGuiCol.Tab]             = header;
        c[(int)ImGuiCol.TabHovered]      = headerHi;
        c[(int)ImGuiCol.TabActive]       = accentDim;

        c[(int)ImGuiCol.ScrollbarBg]          = new Vector4(0.13f, 0.13f, 0.13f, 1.00f);
        c[(int)ImGuiCol.ScrollbarGrab]        = new Vector4(0.32f, 0.32f, 0.32f, 1.00f);
        c[(int)ImGuiCol.ScrollbarGrabHovered] = fieldHi;
        c[(int)ImGuiCol.ScrollbarGrabActive]  = new Vector4(0.42f, 0.42f, 0.42f, 1.00f);

        c[(int)ImGuiCol.TextSelectedBg]   = accentDim;
        c[(int)ImGuiCol.NavHighlight]     = accent;
        c[(int)ImGuiCol.DragDropTarget]   = accent;
    }
}