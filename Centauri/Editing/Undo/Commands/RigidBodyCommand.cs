namespace Centauri.Editing.Undo;

using World;
using Loading;
using Simulation.Physics;

// Kind/Shape at some point in time — RigidBodyCommand's before/after pair. Mass isn't captured
// here: it's a plain per-frame drag like any other DragRow, so it already goes through the
// generic FieldEditCommand<float> mechanism (EntityPhysicsSection's Mass row passes a
// CommandHistory straight to Widgets.DragRow) rather than this command at all.
internal readonly record struct RigidBodyState(BodyKind Kind, BodyShape Shape)
{
    public static RigidBodyState Of(RigidBody rb) => new(rb.Kind, rb.Shape);
}

// One completed Body-kind or Shape combo change (EntityPhysicsSection) — attach, detach, or
// switch Dynamic/Static/Box/Sphere on the selected entity's RigidBody. A null state means "no
// RigidBody attached." Unlike a plain field swap, both directions need to replay the same side
// effects the live edit itself performs: PhysicsSystem only rebuilds a body when it sees
// RigidBody.Dirty on its next Sync, and EntitySetLoader tracks the component's own saved
// definition separately from the live Scene, so both Undo and Redo go through the same
// MarkDirty()/SyncRigidBodyDefinition() calls the inspector's own edit path uses, not just a
// value restore.
internal sealed class RigidBodyCommand : ICommand
{
    private readonly Entity _entity;
    private readonly EntitySetLoader _entitySetLoader;
    private readonly RigidBodyState? _before;
    private readonly RigidBodyState? _after;

    public RigidBodyCommand(Entity entity, EntitySetLoader entitySetLoader, RigidBodyState? before, RigidBodyState? after)
    {
        _entity          = entity;
        _entitySetLoader = entitySetLoader;
        _before          = before;
        _after           = after;
    }

    public void Undo() => Apply(_before);
    public void Redo() => Apply(_after);

    private void Apply(RigidBodyState? state)
    {
        var rb = _entity.GetComponent<RigidBody>();

        if (state is not { } s)
        {
            if (rb is not null)
                _entity.RemoveComponent<RigidBody>();
            _entitySetLoader.SyncRigidBodyDefinition(_entity, null);
            return;
        }

        rb ??= _entity.AddComponent(new RigidBody());
        rb.Kind  = s.Kind;
        rb.Shape = s.Shape;
        rb.MarkDirty();
        _entitySetLoader.SyncRigidBodyDefinition(_entity, rb);
    }
}
