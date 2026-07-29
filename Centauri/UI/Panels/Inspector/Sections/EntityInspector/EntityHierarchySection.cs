namespace Centauri.UI.Panels.Inspector.Sections;

using ImGuiNET;

using World;
using Common;
using Loading;
using Editing.Undo;

// Parent picker — the only authoring path for Transform hierarchy this pass (no
// drag-and-drop reparenting in the Outliner yet; see Docs/Documentation/TransformHierarchy.md
// "Known limitations"). Rebuilds its option list from the live scene every draw rather than
// lazily-caching like the model/material pickers elsewhere in this file — unlike those
// registries, which model exists doesn't change while a scene is open, but which *entities*
// exist does (add/delete). Excludes the entity itself and every one of its own descendants
// from the list up front, so an invalid selection simply isn't offered — cheaper for the user
// than picking one and having EntitySetLoader.SetParent silently refuse it.
internal sealed class EntityHierarchySection
{
    private readonly EntitySetLoader _entitySetLoader;

    public EntityHierarchySection(EntitySetLoader entitySetLoader) => _entitySetLoader = entitySetLoader;

    public void Draw(Entity entity, Scene scene, CommandHistory? undo)
    {
        using var s = Widgets.Section("Hierarchy");
        if (!s.Open) return;

        var names = new List<string> { "(None)" };
        var candidates = new List<Entity?> { null };

        foreach (var other in scene.Entities)
        {
            if (ReferenceEquals(other, entity)) continue;
            if (WouldCreateCycle(other.Transform, entity.Transform)) continue;

            names.Add(other.Name);
            candidates.Add(other);
        }

        var currentParent = entity.Transform.Parent is { } p ? FindOwner(p, scene) : null;
        var index = Math.Max(0, candidates.IndexOf(currentParent));

        if (Widgets.ComboRow("Parent", ref index, names.ToArray()))
        {
            var target = candidates[index];
            if (_entitySetLoader.SetParent(entity, target))
                undo?.Push(new ReparentCommand(_entitySetLoader, entity, currentParent, target));
        }

        if (entity.Transform.Children.Count > 0)
            ImGui.TextDisabled($"{entity.Transform.Children.Count} child(ren)");
    }

    private static Entity? FindOwner(Transform transform, Scene scene)
    {
        foreach (var e in scene.Entities)
            if (ReferenceEquals(e.Transform, transform))
                return e;
        return null;
    }

    // Would entity.Transform.Parent = candidate create a cycle? True when candidate is entity
    // itself or already a descendant of it — mirrors Transform's own private IsAncestorOf check
    // (duplicated here, not exposed, since this is a display-filtering concern, not something
    // Transform's public API needs to answer for its own sake).
    private static bool WouldCreateCycle(Transform candidate, Transform entity)
    {
        for (var current = candidate; current != null; current = current.Parent)
            if (current == entity)
                return true;
        return false;
    }
}
