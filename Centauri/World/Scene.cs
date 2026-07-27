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

    // Ordered (insertion order, not scene order) so Shift-range-select in the Outliner has a
    // stable "last thing you touched" anchor to extend from, and so a plain click always ends up
    // as the sole entry regardless of how big the set was before it. Small in practice (a user's
    // selection, not the whole scene), so a List's O(n) Contains/Remove is fine — no need for a
    // HashSet's O(1) at this scale.
    private readonly List<Entity> _selected = [];
    public IReadOnlyList<Entity> SelectedEntities => _selected;

    // The "primary" selection — the Outliner's most recent plain/Ctrl-click, or the last entity a
    // Shift-range-select added. What the Properties panel's single-entity inspector shows/edits,
    // and what the gizmo anchors its screen position to when multiple entities are selected (see
    // TransformGizmo — dragging still moves/rotates/scales every selected entity together, this is
    // only about where the handles themselves get drawn). Null when nothing's selected.
    public Entity? Selected => _selected.Count > 0 ? _selected[^1] : null;

    public bool IsSelected(Entity entity) => _selected.Contains(entity);

    // Replaces the whole selection with just this entity (or clears it, for null) — a plain click,
    // in the Outliner or the viewport.
    public void Select(Entity? entity)
    {
        _selected.Clear();
        if (entity is not null) _selected.Add(entity);
    }

    // Adds without disturbing whatever else is already selected — a Shift-range-select extending
    // an existing selection. AddToSelection alone (not exposed) would just be Select's single-entity
    // case with extra steps, so there's no separate single-add method — callers building a range
    // call this in a loop after ClearSelection.
    public void AddToSelection(Entity entity)
    {
        if (!_selected.Contains(entity))
            _selected.Add(entity);
    }

    // Ctrl-click: in the set, out; not in, in. The entity becomes (or stops being) the primary
    // selection either way, so a following Shift-range-select extends from wherever this just
    // toggled — matches the anchor-follows-the-click convention most multi-select lists use.
    public void ToggleSelect(Entity entity)
    {
        if (!_selected.Remove(entity))
            _selected.Add(entity);
    }

    public void ClearSelection() => _selected.Clear();

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
        _selected.Remove(entity);   // keep selection valid
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
        _selected.Clear();
        foreach (var entity in _entities)
            entity.Dispose();
        _entities.Clear();
    }
}