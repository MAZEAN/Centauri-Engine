namespace Centauri.UI.Panels.Toolbar;

using ImGuiNET;
using System.Numerics;

using Config;
using Common;
using Layout;

// The docked full-width top strip — modelled on Blender's topbar. Workspace tabs (Edit / Performance
// / Viewing — see EditorWorkspace) on the left decide which of EditorLayout's panel sets is on
// screen; the viewport's own shading segmented control (Shaded | Normals | Depth | ...) and the
// Fly/Edit camera-mode indicator sit to their right, unchanged from what used to be a separate
// floating ViewportToolbar. Always rendered, in every workspace — it's how you get back to Edit
// from Viewing.
internal sealed class TopBar
{
    private const ImGuiWindowFlags Flags =
        PanelHost.DockedFlags | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar;

    private readonly ImFontPtr _font;
    private readonly AppConfig _config;

    private static readonly EditorWorkspace[] Workspaces      = Enum.GetValues<EditorWorkspace>();
    private static readonly string[]          WorkspaceLabels = Array.ConvertAll(Workspaces, w => w.ToString());

    private static readonly ShadingMode[] Modes      = Enum.GetValues<ShadingMode>();
    private static readonly string[]      ModeLabels = Array.ConvertAll(Modes, m => m.ToString());

    private static readonly ViewMode[] ViewModes      = Enum.GetValues<ViewMode>();
    private static readonly string[]   ViewModeLabels = Array.ConvertAll(ViewModes, m => m.ToString());

    // Depends only on _font (fixed for the toolbar's lifetime), so it's computed once on first use
    // rather than every frame — Enum.GetValues() + N ToString()/CalcTextSize() calls used to run
    // per frame just to find a width that never actually changes.
    private float? _maxModeWidth;

    public EditorWorkspace Workspace { get; set; } = EditorWorkspace.Edit;

    public TopBar(ImFontPtr font, AppConfig config)
    {
        _font   = font;
        _config = config;
    }

    public void Render(LayoutRect rect)
    {
        PanelHost.Place(rect, bgAlpha: 1.0f);

        if (!ImGui.Begin("TopBar", Flags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);

        DrawWorkspaceTabs();
        ImGui.SameLine(0f, Widgets.Scale(20f));
        DrawSegments();

        ImGui.PopFont();
        ImGui.End();
    }

    private void DrawWorkspaceTabs()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1f, 0f));

        for (var i = 0; i < Workspaces.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            var selected = Workspace == Workspaces[i];
            if (selected)
                ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent);

            if (ImGui.Button(WorkspaceLabels[i]))
                Workspace = Workspaces[i];

            if (selected)
                ImGui.PopStyleColor();
        }

        ImGui.PopStyleVar();
    }

    private void DrawSegments()
    {
        // tight, touching buttons so they read as one segmented control
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1f, 0f));

        for (var i = 0; i < Modes.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            var selected = _config.Debug.Shading == Modes[i];
            if (selected)
                ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent);

            if (ImGui.Button(ModeLabels[i]))
                _config.Debug.Shading = Modes[i];

            if (selected)
                ImGui.PopStyleColor();
        }

        ImGui.PopStyleVar();
    }

    private float MaxModeWidth()
    {
        if (_maxModeWidth is { } cached) return cached;

        var w = 0f;
        foreach (var label in ViewModeLabels)
            w = MathF.Max(w, ImGui.CalcTextSize(label).X);

        _maxModeWidth = w;
        return w;
    }
}
