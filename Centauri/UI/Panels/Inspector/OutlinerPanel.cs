namespace Centauri.UI.Panels.Inspector;

using ImGuiNET;
using System.Numerics;

using World;
using Common;

// Scene hierarchy — lists every entity (including light-only ones that can't be
// ray-picked in the viewport) and selects on click. Selection drives the
// PropertiesPanel inspector, which already edits lights/materials/transforms.
public sealed class HierarchyPanel
{
    private const float Width   = 300f;
    private const float Padding = 10f;
    private const float BgAlpha = 0.85f;
    public  const float Height  = 220f;

    private readonly ImFontPtr _font;
    private const ImGuiWindowFlags Flags = Widgets.PanelBase;

    public HierarchyPanel(ImFontPtr font) => _font = font;

    public void Render(Scene scene)
    {
        SetupWindow();

        if (!ImGui.Begin("Outliner", Flags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(_font);
        
        ImGui.BeginChild("entities", new Vector2(0, 0));

        var entities = scene.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            ImGui.PushID(i);
            
            DrawRow(scene, entities[i]);
            
            ImGui.PopID();
        }

        ImGui.EndChild();

        ImGui.PopFont();
        
        ImGui.End();
    }

    private static void DrawRow(Scene scene, Entity entity)
    {
        var selected = ReferenceEquals(scene.Selected, entity);

        var dim = !entity.Enabled;
        if (dim)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);

        if (ImGui.Selectable($"{Tag(entity)}  {entity.Name}", selected))
            scene.Select(entity);

        if (dim)
            ImGui.PopStyleColor();
    }

    private static string Tag(Entity e) => e switch
    {
        { Light: DirectionalLight } => "[Sun]",
        { Light: PointLight }       => "[Point]",
        { Light: SpotLight }        => "[Spot]",
        { Model: not null }         => "[Mesh]",
        _                           => "[Empty]"
    };

    private static void SetupWindow()
    {
        var viewport = ImGui.GetMainViewport();
        
        var anchor = new Vector2(
            viewport.WorkPos.X + viewport.WorkSize.X - Padding,
            viewport.WorkPos.Y + Padding);

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.SetNextWindowSize(new Vector2(Width, Height), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(BgAlpha);
    }
}