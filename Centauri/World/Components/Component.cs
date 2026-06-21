namespace Centauri.World.Components;

// Base for per-entity behavior. Attached to an Entity and ticked each frame by
// the SimulationSystem. Keep each component to one focused behavior.
public abstract class Component
{
    public Entity Owner { get; private set; } = null!;

    public bool Enabled { get; set; } = true;   // disabled components are skipped by Entity.Update

    internal void Attach(Entity owner)
    {
        Owner = owner;
        OnAttach();
    }

    protected virtual void OnAttach() { }
    public    virtual void Update(float dt) { }
}