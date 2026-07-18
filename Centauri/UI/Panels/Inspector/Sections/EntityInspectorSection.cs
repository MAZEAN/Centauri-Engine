namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using World;
using Common;
using Rendering;
using Loading;

// The selected-entity inspector: name/enabled header, then delegates each collapsible block
// (Transform / Hierarchy / Material / Light / Physics) to its own sub-section — see the
// Entity*Section classes in this folder. Shows a placeholder when nothing is selected.
public sealed class EntityInspectorSection : ISection
{
    private readonly EntityTransformSection _transform = new();
    private readonly EntityHierarchySection _hierarchy;
    private readonly EntityMaterialSection  _material;
    private readonly EntityLightSection     _light = new();
    private readonly EntityPhysicsSection   _physics;

    public EntityInspectorSection(ResourceSystem resourceSystem, EntitySetLoader entitySetLoader)
    {
        _hierarchy = new EntityHierarchySection(entitySetLoader);
        _material  = new EntityMaterialSection(resourceSystem, entitySetLoader);
        _physics   = new EntityPhysicsSection(entitySetLoader);
    }

    public void Draw(Scene scene)
    {
        if (scene.Selected is not { } entity)
        {
            ImGui.TextDisabled("No entity selected");
            return;
        }

        DrawHeader(entity);
        Widgets.CheckRow("Enabled", entity.Enabled, v => entity.Enabled = v);
        ImGui.Spacing();

        _transform.Draw(entity);
        _hierarchy.Draw(entity, scene);
        _material.Draw(entity, scene);
        _light.Draw(entity);
        _physics.Draw(entity);
    }

    private static void DrawHeader(Entity e)
    {
        var name = e.Name;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##name", ref name, 64))
            e.Name = name;

        ImGui.Spacing();
    }
}
