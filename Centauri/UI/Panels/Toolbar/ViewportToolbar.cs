namespace Centauri.UI.Panels.Toolbar;

using ImGuiNET;
using System.Numerics;

using Config;
using Common;

// Top-of-viewport segmented control for the viewport shading mode (Blender-style):
// Shaded | Normals | Depth. Mirrors the G shortcut — both drive _config.Debug.Shading,
// and both auto-pick up new ShadingMode entries (AmbientOcclusion, Raytraced, …).
public sealed class ViewportToolbar
{
    private const float Padding = 10f;
    private const float BgAlpha = 0.85f;

    private static readonly ShadingMode[] Modes = Enum.GetValues<ShadingMode>();

    private const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoMove            | ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoTitleBar        | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoSavedSettings   | ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav;

    private readonly ImFontPtr _font;
    private readonly AppConfig _config;

    public ViewportToolbar(ImFontPtr font, AppConfig config)
    {
        _font   = font;
        _config = config;
    }

    public void Render()
    {
        SetupWindow();

        if (!ImGui.Begin("ViewportToolbar", Flags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);
        
        DrawSegments();
        DrawModeIndicator();
        
        ImGui.PopFont();

        ImGui.End();
    }

    private void DrawSegments()
    {
        // tight, touching buttons so they read as one segmented control
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1f, 0f));

        for (var i = 0; i < Modes.Length; i++)
        {
            if (i > 0) ImGui.SameLine();

            var selected = _config.Debug.Shading == Modes[i];
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent);

            if (ImGui.Button(Modes[i].ToString()))
                _config.Debug.Shading = Modes[i];

            if (selected) ImGui.PopStyleColor();
        }

        ImGui.PopStyleVar();
    }
    
    private void DrawModeIndicator()
    {
        ImGui.SameLine(0f, 16f);
        ImGui.AlignTextToFramePadding();

        var mode  = _config.Input.Mode;
        var color = mode == ViewMode.Fly ? ColorPalette.Amber : ColorPalette.Green;
        var label = mode.ToString();

        ImGui.TextColored(color, label);

        // pad to the widest mode name so the toolbar width doesn't jump when the mode changes
        var pad = MaxModeWidth() - ImGui.CalcTextSize(label).X;
        if (pad > 0f)
        {
            ImGui.SameLine(0f, 0f);
            ImGui.Dummy(new Vector2(pad, 0f));
        }
    }

    private static float MaxModeWidth()
    {
        var w = 0f;
        foreach (var m in Enum.GetValues<ViewMode>())
            w = MathF.Max(w, ImGui.CalcTextSize(m.ToString()).X);
        
        return w;
    }

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();
        var anchor = new Vector2(
            viewport.WorkPos.X + viewport.WorkSize.X * 0.5f,   // horizontal centre
            viewport.WorkPos.Y + Padding);                     // top edge

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(0.5f, 0f)); // pivot top-centre
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }
}
