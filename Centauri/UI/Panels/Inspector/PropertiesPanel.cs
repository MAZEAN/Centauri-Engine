namespace Centauri.UI.Panels.Inspector;

using ImGuiNET;
using System.Numerics;

using World;
using Config;
using Common;
using Layout;
using Sections;
using Rendering;
using Loading;

internal readonly record struct SectionGroup(string Name, Vector4 Accent, ISection[] Sections);

// Docked into EditorLayout's Properties rect (see EditorLayout.cs), directly beneath
// HierarchyPanel — the two touch with no gap, part of the Edit workspace's exact tiling.
internal class PropertiesPanel
{
    private readonly ImFontPtr _font;
    private readonly AppConfig _config;

    private readonly EntityInspectorSection _entitySection;
    private readonly SectionGroup[] _groups;

    public PropertiesPanel(ImFontPtr font, AppConfig config, ResourceSystem resourceSystem, EntitySetLoader entitySetLoader)
    {
        _font = font;
        _config = config;
        _entitySection = new EntityInspectorSection(resourceSystem, entitySetLoader);
        _groups =
        [
            new SectionGroup("Environment", ColorPalette.Green, [
                new SkyboxSection(),
                new SkySection(config.Sky),
                new DayNightSection(),
                new ShadowSection(config.Shadows),
                new SpotShadowSection(config.SpotShadows),
                new FoliageSection(config.Foliage)
            ]),
            new SectionGroup("Reflections", ColorPalette.Blue, [
                new IBLSection(config.IBL),
                new ReflectionProbeSection(config.ReflectionProbe),
                new PlanarReflectionSection(config.PlanarReflection),
                new SSRSection(config.SSR)
            ]),
            new SectionGroup("Post FX", ColorPalette.Purple, [
                new GTAOSection(config.GTAO),
                new TAASection(config.TAA),
                new BloomSection(config.Bloom),
                new AutoExposureSection(config.AutoExposure),
                new ColorGradingSection(config.ColorGrading)
            ]),
            new SectionGroup("Scene", ColorPalette.Amber, [
                new CullingSection(config),
                new ViewportSection(config.Debug, config.Render),
                new TracySection(config.Debug),
                new PanelAppearanceSection(config.ImGui)
            ])
        ];
    }

    public void Render(Scene scene, LayoutRect rect)
    {
        PanelHost.Place(rect, bgAlpha: _config.ImGui.PropertiesAlpha);

        if (!ImGui.Begin("Properties", PanelHost.DockedFlags))
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

}
