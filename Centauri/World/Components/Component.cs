namespace Centauri.World.Components;

// Base for per-entity behavior. Attached to an Entity and ticked each frame by
// the SimulationSystem. Keep each component to one focused behavior.
public abstract class Component
{
    public Entity Owner { get; private set; } = null!;

    internal void Attach(Entity owner)
    {
        Owner = owner;
        OnAttach();
    }

    protected virtual void OnAttach() { }   // one-time setup once Owner is set
    public    virtual void Update(float dt) { }
}