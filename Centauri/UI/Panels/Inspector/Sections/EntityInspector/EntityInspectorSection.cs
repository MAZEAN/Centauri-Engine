namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using World;
using Common;
using Rendering;
using Loading;
using Editing.Undo;

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
    private readonly CommandHistory _commandHistory;

    public EntityInspectorSection(ResourceSystem resourceSystem, EntitySetLoader entitySetLoader, CommandHistory commandHistory)
    {
        _hierarchy = new EntityHierarchySection(entitySetLoader);
        _material  = new EntityMaterialSection(resourceSystem, entitySetLoader, commandHistory);
        _physics   = new EntityPhysicsSection(entitySetLoader);
        _commandHistory = commandHistory;
    }

    public void Draw(Scene scene)
    {
        if (scene.Selected is not { } entity)
        {
            ImGui.TextDisabled("No entity selected");
            return;
        }

        DrawHeader(entity);

        // Multi-select (Scene.SelectedEntities) edits every selected entity together via the
        // gizmo and bulk-deletes them all, but this inspector still only displays/edits the one
        // *primary* entity's own properties — building real multi-entity property editing (what
        // happens when the same field differs across the selection) is its own feature. This just
        // makes it visible that there's more selected than what's shown, rather than silently
        // looking like a single-select.
        if (scene.SelectedEntities.Count > 1)
            ImGui.TextDisabled($"+{scene.SelectedEntities.Count - 1} more selected");

        Widgets.CheckRow("Enabled", entity.Enabled, v => entity.Enabled = v);
        ImGui.Spacing();

        _transform.Draw(entity, _commandHistory);
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
