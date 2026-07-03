namespace Centauri.UI.Panels.Inspector;

using ImGuiNET;
using System.Numerics;

using World;
using Config;
using Common;
using Sections;

internal readonly record struct SectionGroup(string Name, Vector4 Accent, ISection[] Sections);

public class PropertiesPanel
{
    private const float Width   = 300f;
    private const float Padding = 10f;
    private const float BgAlpha = 0.85f;

    private const ImGuiWindowFlags Flags = Widgets.PanelBase;

    private readonly ImFontPtr _font;
    
    private readonly EntityInspectorSection _entitySection = new();
    private readonly SectionGroup[] _groups;

    public PropertiesPanel(ImFontPtr font, AppConfig config)
    {
        _font = font;
        _groups =
        [
            new SectionGroup("Environment", ColorPalette.Green, [
                new SkyboxSection(),
                new ShadowSection(config),
                new WindSection(config)
            ]),
            new SectionGroup("Reflections", ColorPalette.Blue, [
                new IBLSection(config),
                new ReflectionProbeSection(config),
                new PlanarReflectionSection(config),
                new SSRSection(config)
            ]),
            new SectionGroup("Post FX", ColorPalette.Purple, [
                new SSAOSection(config),
                new TAASection(config),
                new BloomSection(config),
                new ColorGradingSection(config.ColorGrading)
            ]),
            new SectionGroup("Scene", ColorPalette.Amber, [
                new CullingSection(config),
                new ViewportSection(config)
            ])
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

        DrawGroups(scene);

        ImGui.PopFont();

        ImGui.End();
    }

    private void DrawGroups(Scene scene)
    {
        _entitySection.Draw(scene);
        ImGui.Separator();
        ImGui.Spacing();

        // Groups stack vertically: each is a colored, collapsible header with its sections
        // indented beneath it.
        foreach (var group in _groups)
        {
            var open = Widgets.BeginPanel(group.Name, group.Accent);
            if (open)
                foreach (var section in group.Sections)
                    section.Draw(scene);
            Widgets.EndPanel(open);
        }
    }

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();

        // stack beneath the outliner: outliner padding + height + a gap
        var top = viewport.WorkPos.Y + Padding + HierarchyPanel.Height + Padding;
        var anchor = new Vector2(viewport.WorkPos.X + viewport.WorkSize.X - Padding, top);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f));   // pivot top-right
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(Width, 0),
            new Vector2(Width, viewport.WorkPos.Y + viewport.WorkSize.Y - Padding - top));   // fill to bottom edge
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }
}
