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
                new SkySection(config.Sky),
                new DayNightSection(),
                new ShadowSection(config.Shadows),
                new WindSection(config.Wind)
            ]),
            new SectionGroup("Reflections", ColorPalette.Blue, [
                new IBLSection(config.IBL),
                new ReflectionProbeSection(config.ReflectionProbe),
                new PlanarReflectionSection(config.PlanarReflection),
                new SSRSection(config.SSR)
            ]),
            new SectionGroup("Post FX", ColorPalette.Purple, [
                new SSAOSection(config.SSAO),
                new TAASection(config.TAA),
                new BloomSection(config.Bloom),
                new AutoExposureSection(config.AutoExposure),
                new ColorGradingSection(config.ColorGrading)
            ]),
            new SectionGroup("Scene", ColorPalette.Amber, [
                new CullingSection(config),
                new ViewportSection(config.Debug),
                new TracySection(config.Debug)
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
        var padding  = Widgets.Scale(Padding);
        var width    = Widgets.Scale(Width);

        // stack beneath the outliner: outliner padding + height + a gap
        var top = viewport.WorkPos.Y + padding + HierarchyPanel.Height + padding;
        var anchor = new Vector2(viewport.WorkPos.X + viewport.WorkSize.X - padding, top);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f));   // pivot top-right
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(width, 0),
            new Vector2(width, viewport.WorkPos.Y + viewport.WorkSize.Y - padding - top));   // fill to bottom edge
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }
}
