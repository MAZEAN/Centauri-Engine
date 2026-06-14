namespace Centauri.World;

using Rendering.Systems;
using Utils.Geometry;
using Collections;

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
        // cameras have no unmanaged state; cubeMaps are owned/disposed by ResourceSystem
    }
}