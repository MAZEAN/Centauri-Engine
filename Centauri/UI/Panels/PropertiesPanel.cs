namespace Centauri.UI.Panels;

using ImGuiNET;
using System.Numerics;

using World;
using Config;
using Common;
using Inspector;

// Hosts the Properties window chrome and drives an ordered list of inspector sections.
// Adding a section is one entry in the list below + a class in Panels/Inspector/.
public class PropertiesPanel
{
    private const float Width   = 300f;
    private const float Padding = 10f;
    private const float BgAlpha = 0.85f;

    private const ImGuiWindowFlags Flags = Widgets.PanelBase;

    private readonly ImFontPtr _font;
    private readonly IInspectorSection[] _sections;

    public PropertiesPanel(ImFontPtr font, AppConfig config, ColorGrading grading)
    {
        _font = font;
        _sections =
        [
            new EntityInspectorSection(),
            new SkyboxSection(),
            new ShadowSection(config),
            new SSAOSection(config),
            new SSRSection(config),
            new BloomSection(config),
            new ColorGradingSection(grading),
            new IBLSection(config),
            new ViewportSection(config),
        ];
    }

    public void Render(Scene scene)
    {
        SetupWindow();

        if (!ImGui.Begin("Properties", Flags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);

        foreach (var section in _sections)
            section.Draw(scene);

        ImGui.PopFont();

        ImGui.End();
    }

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();

        // stack beneath the outliner: outliner padding + height + a gap
        var top = viewport.WorkPos.Y + Padding + OutlinerPanel.Height + Padding;
        var anchor = new Vector2(viewport.WorkPos.X + viewport.WorkSize.X - Padding, top);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f));   // pivot top-right
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(Width, 0),
            new Vector2(Width, viewport.WorkPos.Y + viewport.WorkSize.Y - Padding - top));   // fill to bottom edge
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }
}
