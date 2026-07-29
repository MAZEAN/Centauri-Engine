namespace Centauri.Editing.Undo;

using World;
using Loading;

// One completed edit of the Hierarchy section's Parent picker (EntityHierarchySection) — a
// single combo selection, so (unlike TransformCommand's drag gesture) there's no multi-frame
// span to capture: "before" and "after" are just the parent Entity? read immediately either side
// of the one EntitySetLoader.SetParent call the picker itself makes. Both directions replay
// through SetParent rather than poking Transform.Parent directly, so a redo that would recreate a
// cycle (only possible if the scene changed shape between the original edit and the redo — the
// picker itself already filters cycle-creating candidates out of its list) is refused the same
// way the live edit was.
internal sealed class ReparentCommand : ICommand
{
    private readonly EntitySetLoader _entitySetLoader;
    private readonly Entity _entity;
    private readonly Entity? _before;
    private readonly Entity? _after;

    public ReparentCommand(EntitySetLoader entitySetLoader, Entity entity, Entity? before, Entity? after)
    {
        _entitySetLoader = entitySetLoader;
        _entity          = entity;
        _before          = before;
        _after           = after;
    }

    public void Undo() => _entitySetLoader.SetParent(_entity, _before);
    public void Redo() => _entitySetLoader.SetParent(_entity, _after);
}
