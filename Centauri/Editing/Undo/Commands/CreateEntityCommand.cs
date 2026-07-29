namespace Centauri.Editing.Undo;

using Loading;
using World;

// One Outliner "+ Add" (HierarchyPanel.DrawAddRow). Undo deletes the created entity; Redo rebuilds
// it from the same (modelId, materialId, name) inputs — a fresh Entity instance each time, not the
// same object reused across the cycle (EntitySetLoader.DeleteEntity disposes it, and Entity isn't
// designed to come back from Dispose — see Entity.Dispose unsubscribing its own Transform.OnChanged
// handler). Entity is mutable for exactly that reason: it always points at whichever instance is
// currently live, so a later command capturing `Entity.Transform` at construction time — nothing
// does today, but see Docs/Documentation/Undo.md's note on this — would need to re-resolve it,
// not assume the original reference still exists.
internal sealed class CreateEntityCommand : ICommand
{
    private readonly EntitySetLoader _loader;
    private readonly string? _modelId;
    private readonly string? _materialId;
    private readonly string _name;

    public Entity Entity { get; private set; }

    public CreateEntityCommand(EntitySetLoader loader, Entity entity, string? modelId, string? materialId, string name)
    {
        _loader     = loader;
        Entity      = entity;
        _modelId    = modelId;
        _materialId = materialId;
        _name       = name;
    }

    public void Undo() => _loader.DeleteEntity(Entity);
    public void Redo() => Entity = _loader.CreateEntity(_modelId, _materialId, _name);
}
