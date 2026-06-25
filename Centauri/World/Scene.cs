namespace Centauri.World;

using Utils.Geometry;
using Collections;
using Components;

public class Scene
{
    private readonly List<Entity> _entities = new();
    public IReadOnlyList<Entity> Entities => _entities;
    public int Revision { get; private set; }

    public LightingSystem Lighting  { get; } = new();
    public CameraRig      Cameras   { get; } = new();
    public SkyboxSet      Skyboxes  { get; } = new();

    public Entity? Selected { get; private set; }
    public void Select(Entity? entity) => Selected = entity;
    public void ClearSelection()       => Selected = null;
    
    public void MarkDirty() => Revision++;

    public void AddEntity(Entity entity)
    {
        _entities.Add(entity);
        Revision++;
    }

    public void RemoveEntity(Entity entity)
    {
        _entities.Remove(entity);
        if (Selected == entity) Selected = null;   // keep selection valid
        Revision++;
    }
    
    // first component of type T across the scene, or null — handy for global toggles
    public T? FindComponent<T>() where T : Component
    {
        foreach (var e in _entities)
            if (e.GetComponent<T>() is { } c)
                return c;
        return null;
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
        foreach (var entity in _entities) entity.Dispose();
        _entities.Clear();
    }
}