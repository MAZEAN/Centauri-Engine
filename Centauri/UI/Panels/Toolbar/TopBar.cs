namespace Centauri.UI.Panels.Toolbar;

using ImGuiNET;
using System.Numerics;

using Config;
using Common;
using Layout;

// The docked full-width top strip — modelled on Blender's topbar. Workspace tabs (Edit / Performance
// / Viewing — see EditorWorkspace) on the left decide which of EditorLayout's panel sets is on
// screen; the viewport's own shading segmented control (Shaded | Normals | Depth | ...) sits to
// their right, unchanged from what used to be a separate floating ViewportToolbar. Always rendered,
// in every workspace — it's how you get back to Edit from Viewing. There's deliberately no separate
// Fly/Edit camera-mode text indicator here — UISystem couples the two: Tab into Fly auto-switches to
// the Viewing workspace (nothing to click while the cursor's captured for camera look anyway) and
// restores whatever workspace was active when you Tab back to Edit, so the workspace tabs
// themselves are the indicator instead of a second readout next to them.
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

    public EditorWorkspace Workspace { get; set; } = EditorWorkspace.Edit;

    public TopBar(ImFontPtr font, AppConfig config)
    {
        _font   = font;
        _config = config;
    }

    public void Render(LayoutRect rect)
    {
        HandleWorkspaceHotkeys();

        PanelHost.Place(rect, bgAlpha: _config.ImGui.TopBarAlpha);

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

    // P jumps straight to the Performance workspace — the one workspace switch worth a hotkey (Edit
    // is the default you start in and Viewing is one click away on the always-visible tabs). Doesn't
    // reuse Tab: that's already Config.Input.ToggleModeKey for Fly/Edit camera mode, read off the raw
    // Silk.NET keyboard in InputSystem.OnKeyDown with no modifier check at all, so even a Ctrl/Shift
    // combo on Tab would still fire the camera toggle alongside whatever we bound it to here. Read
    // every frame regardless of which workspace is active or the camera's Fly/Edit mode — unlike the
    // gizmo's W/E/R mode switch, jumping to Performance while flying is exactly the point.
    private void HandleWorkspaceHotkeys()
    {
        var io = ImGui.GetIO();
        if (io.WantCaptureKeyboard || io.KeyCtrl || io.KeyShift || io.KeyAlt) return;

        if (ImGui.IsKeyPressed(ImGuiKey.P, repeat: false))
            Workspace = EditorWorkspace.Performance;
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

}
