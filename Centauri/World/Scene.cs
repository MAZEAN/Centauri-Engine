namespace Centauri.World;

using Utils.Geometry;
using Collections;
using Components;

public class Scene
{
    private readonly List<Entity> _entities = [];
    public IReadOnlyList<Entity> Entities => _entities;
    public int Revision { get; private set; }

    public LightingSystem Lighting  { get; } = new();
    public CameraRig      Cameras   { get; } = new();
    public SkyboxSet      Skyboxes  { get; } = new();
    
    private Type?      _lastFindType;
    private Component? _lastFindResult;
    private int         _lastFindRevision = -1;

    public Entity? Selected { get; private set; }
    public void Select(Entity? entity) => Selected = entity;
    public void ClearSelection()       => Selected = null;
    
    public void MarkDirty() => Revision++;

    // CullingSystem's SpatialGrid buckets entities by world position at whatever Revision it
    // last rebuilt against — without this, moving an entity (inspector drag, or a runtime
    // CreateEntity() placement moved away from its spawn point) leaves it registered under its
    // *old* cell forever, since add/remove were the only things that bumped Revision. Once that
    // stale cell falls outside the frustum, Cull()'s coarse per-cell test skips the entity before
    // ever reaching its own (always up to date) bounds check — it silently vanishes despite
    // actually being in view. No currently-shipped Component mutates Transform every frame
    // (SunOrbit/DayNightCycle only touch Light), so this stays sparse/user-driven in practice;
    // if that ever changes, a component driving Transform every frame would force a full grid
    // rebuild every frame too — worth revisiting then, not preemptively here.
    public void AddEntity(Entity entity)
    {
        _entities.Add(entity);
        entity.Transform.OnChanged += MarkDirty;
        Revision++;
    }

    public void RemoveEntity(Entity entity)
    {
        entity.Transform.OnChanged -= MarkDirty;
        _entities.Remove(entity);
        if (Selected == entity)
            Selected = null;   // keep selection valid
        Revision++;
    }

    // Disposes and drops every entity — the live-scene half of EntitySetLoader.Reset(), which
    // reloads from disk right after. Distinct from Dispose() (final teardown, doesn't bump
    // Revision since nothing survives it to care) by actually leaving the scene usable
    // afterward.
    public void ClearEntities()
    {
        foreach (var entity in _entities)
        {
            entity.Transform.OnChanged -= MarkDirty;
            entity.Dispose();
        }
        _entities.Clear();
        Selected = null;
        Revision++;
    }

    // first component of type T across the scene, or null — handy for global toggles
    public T? FindComponent<T>() where T : Component
    {
        if (_lastFindRevision == Revision && _lastFindType == typeof(T))
            return (T?)_lastFindResult;

        T? found = null;
        foreach (var e in _entities)
            if (e.GetComponent<T>() is { } c)
            {
                found = c; 
                break;
            }

        _lastFindType     = typeof(T);
        _lastFindResult   = found;
        _lastFindRevision = Revision;
        
        return found;
    }

    public Entity? Pick(Ray ray)
    {
        Entity? hit = null;
        var best = float.MaxValue;

        foreach (var e in _entities)
        {
            if (!e.Enabled || e.Model is null) continue;   // only renderable entities are pickable
            if (!e.GetWorldBounds().Intersects(ray, out var t) || !(t >= 0f) || !(t < best)) continue;

            best = t;
            hit  = e;
        }
        return hit;
    }

    public void Dispose()
    {
        Selected = null;
        foreach (var entity in _entities) 
            entity.Dispose();
        _entities.Clear();
    }
}