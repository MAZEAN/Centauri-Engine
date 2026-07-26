namespace Centauri.Editing.Undo;

using Loading;
using World;

// One Delete-key press (InputSystem.OnKeyDown, gated on Edit mode + a selection — same gate the
// delete itself already used). Captures the entity's EntityDefinition (EntitySetLoader.Capture —
// the same snapshot Save() itself would write to disk) and its tracked source file *before*
// deleting, so Undo can rebuild an equivalent entity through EntitySetLoader.Restore. Doesn't
// attempt to restore any children the deleted entity had — DeleteEntity already promotes them to
// the scene root as a permanent side effect before this command even sees them, and re-linking
// that is out of scope for this first, coarse pass (see Docs/Documentation/Undo.md).
internal sealed class DeleteEntityCommand : ICommand
{
    private readonly EntitySetLoader _loader;
    private readonly EntityDefinition _definition;
    private readonly string _sourcePath;

    public Entity Entity { get; private set; }

    public DeleteEntityCommand(EntitySetLoader loader, Entity entity, EntityDefinition definition, string sourcePath)
    {
        _loader     = loader;
        Entity      = entity;
        _definition = definition;
        _sourcePath = sourcePath;
    }

    public void Undo() => Entity = _loader.Restore(_definition, _sourcePath);
    public void Redo() => _loader.DeleteEntity(Entity);
}
