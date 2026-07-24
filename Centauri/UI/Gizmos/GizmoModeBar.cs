namespace Centauri.UI.Gizmos;

using System.Numerics;
using ImGuiNET;

using Common;
using Layout;

// Blender-style left-edge tool column for the transform gizmo's mode — Move / Rotate / Scale,
// stacked vertically. Mirrors the W/E/R shortcuts (both drive TransformGizmo.ActiveMode) and
// doubles as the mode *indicator*: the active tool's button is highlighted. The glyphs are
// vector-drawn into each button's rect (no icon font is loaded in this project), echoing the gizmo
// itself — a 4-way arrow, a curved arrow, and a box-tipped diagonal.
//
// Docked into EditorLayout's LeftTools rect (see EditorLayout.cs) rather than floating — it's part
// of the Edit workspace's exact tiling, not an overlay on top of it.
internal sealed class GizmoModeBar
{
    private const float ButtonSize = 34f;
    private const float Rounding   = 4f;
    private const float TopPadding = 8f; // small gap below the top strip, matching the button spacing

    private const ImGuiWindowFlags Flags =
        PanelHost.DockedFlags | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

    private static readonly (TransformGizmo.Mode Mode, string Tip)[] Tools =
    [
        (TransformGizmo.Mode.Translate, "Move (W)"),
        (TransformGizmo.Mode.Rotate,    "Rotate (E)"),
        (TransformGizmo.Mode.Scale,     "Scale (R)"),
    ];

    private readonly ImFontPtr _font;
    private readonly TransformGizmo _gizmo;

    public GizmoModeBar(ImFontPtr font, TransformGizmo gizmo)
    {
        _font  = font;
        _gizmo = gizmo;
    }

    public void Render(LayoutRect rect)
    {
        PanelHost.Place(rect, bgAlpha: 1.0f);

        if (!ImGui.Begin("GizmoModeBar", Flags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);
        ImGui.Dummy(new Vector2(1f, Widgets.Scale(TopPadding)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Widgets.Scale(6f)));

        // Center the column of buttons horizontally within whatever width the tool column ended
        // up with (it's clamped down at very small window sizes — see EditorLayout).
        var buttonSize = MathF.Min(Widgets.Scale(ButtonSize), rect.Size.X);
        var indent     = MathF.Max(0f, (rect.Size.X - buttonSize) * 0.5f);

        foreach (var (mode, tip) in Tools)
        {
            ImGui.SetCursorPosX(indent);
            DrawToolButton(mode, tip, buttonSize);
        }

        ImGui.PopStyleVar();
        ImGui.PopFont();
        ImGui.End();
    }

    private void DrawToolButton(TransformGizmo.Mode mode, string tip, float buttonSize)
    {
        var size = new Vector2(buttonSize);
        var p0   = ImGui.GetCursorScreenPos();

        ImGui.PushID((int)mode);
        var clicked = ImGui.InvisibleButton("btn", size);
        var hovered = ImGui.IsItemHovered();
        ImGui.PopID();

        var active = _gizmo.ActiveMode == mode;
        var dl     = ImGui.GetWindowDrawList();

        var bg = active ? ColorPalette.Accent : hovered ? ColorPalette.FieldHi : ColorPalette.Field;
        dl.AddRectFilled(p0, p0 + size, ImGui.GetColorU32(bg), Widgets.Scale(Rounding));

        var iconColor = ImGui.GetColorU32(active ? ColorPalette.White : ColorPalette.Text);
        DrawIcon(dl, mode, p0 + size * 0.5f, buttonSize * 0.30f, iconColor);

        if (hovered)
            ImGui.SetTooltip(tip);
        if (clicked)
            _gizmo.ActiveMode = mode;
    }

    // ---- vector icons, centered at c with extent e ---------------------------------------------
    private static void DrawIcon(ImDrawListPtr dl, TransformGizmo.Mode mode, Vector2 c, float e, uint col)
    {
        var th = MathF.Max(1.5f, e * 0.16f);
        switch (mode)
        {
            case TransformGizmo.Mode.Translate:
                MoveIcon(dl, c, e, th, col); break;
            case TransformGizmo.Mode.Rotate:
                RotateIcon(dl, c, e, th, col); break;
            default:
                ScaleIcon(dl, c, e, th, col); break;
        }
    }

    private static void MoveIcon(ImDrawListPtr dl, Vector2 c, float e, float th, uint col)
    {
        var x = new Vector2(e, 0f);
        var y = new Vector2(0f, e);

        dl.AddLine(c - x, c + x, col, th);
        dl.AddLine(c - y, c + y, col, th);

        Arrowhead(dl, c + x, new Vector2(1f, 0f),  e, col);
        Arrowhead(dl, c - x, new Vector2(-1f, 0f), e, col);
        Arrowhead(dl, c + y, new Vector2(0f, 1f),  e, col);
        Arrowhead(dl, c - y, new Vector2(0f, -1f), e, col);
    }

    private static void RotateIcon(ImDrawListPtr dl, Vector2 c, float e, float th, uint col)
    {
        const int n = 20;
        const float a0 = -2.3f, a1 = 2.3f; // ~260° open arc

        var prev = default(Vector2);
        for (var k = 0; k <= n; k++)
        {
            var a = a0 + (a1 - a0) * (k / (float)n);
            var p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * e;

            if (k > 0)
                dl.AddLine(prev, p, col, th);
            prev = p;
        }

        var tangent = new Vector2(-MathF.Sin(a1), MathF.Cos(a1)); // direction of increasing angle
        Arrowhead(dl, prev, tangent, e, col);
    }

    private static void ScaleIcon(ImDrawListPtr dl, Vector2 c, float e, float th, uint col)
    {
        var d = new Vector2(e * 0.8f, e * 0.8f);
        var a = c - d;
        var b = c + d;

        dl.AddLine(a, b, col, th);
        Box(dl, a, e * 0.34f, col);
        Box(dl, b, e * 0.34f, col);
    }

    private static void Arrowhead(ImDrawListPtr dl, Vector2 tip, Vector2 dir, float e, uint col)
    {
        var a    = e * 0.5f;
        var perp = new Vector2(-dir.Y, dir.X);
        var b    = tip - dir * a;

        dl.AddTriangleFilled(tip, b + perp * (a * 0.6f), b - perp * (a * 0.6f), col);
    }

    private static void Box(ImDrawListPtr dl, Vector2 centre, float h, uint col) =>
        dl.AddRectFilled(centre - new Vector2(h), centre + new Vector2(h), col);
}
