namespace Centauri.Rendering.Culling;

using World;
using Utils.Geometry;

// Owns the scene's spatial grid and the per-frame visible set. Updated once a frame so the
// prepass and forward pass share one frustum-cull result instead of each re-testing every
// entity. The grid is exposed for a debug overlay; the visible set drives the passes.
public sealed class CullingSystem
{
    private const float Tolerance = 0.001f;
    
    private float _cellSize;
    private float _oversizeFactor;
    
    public SpatialGrid Grid { get; private set; }
    private readonly HashSet<Entity> _visible = [];
    
    private int   _builtRevision = -1;
    private bool  _enabled = true;
    
    public IReadOnlyCollection<Entity> Visible => _visible;
    public bool Enabled => _enabled;
    public int EntityCount => Grid.EntityCount;

    public CullingSystem(float cellSize = 16f, float oversizeFactor = 8f)
    {
        _cellSize       = cellSize;
        _oversizeFactor = oversizeFactor;
        
        Grid           = new SpatialGrid(cellSize, oversizeFactor);
    }
    
    // Rebuild the grid when the scene changes (revision-gated, like the shadow bounds cache —
    // runtime transform animation must bump the revision), then cull against the camera.
    public void Update(Scene scene, Camera camera, bool enabled, float cellSize, float oversizeFactor)
    {
        camera.UpdateFrustum();
        Update(scene, camera.Frustum, enabled, cellSize, oversizeFactor);
    }
    
    // Same as above but against an arbitrary frustum instead of a Camera's own — lets a pass
    // that renders from a derived view (e.g. a mirrored planar-reflection camera) cull against
    // the frustum it's actually rendering with, instead of skipping culling altogether.
    public void Update(Scene scene, Frustum frustum, bool enabled, float cellSize, float oversizeFactor)
    {
        using var _ = Profiling.Tracy.Scope("CullingSystem.Update");

        _enabled = enabled;
        
        if (Math.Abs(cellSize - _cellSize) > Tolerance || Math.Abs(oversizeFactor - _oversizeFactor) > Tolerance)
        {
            _cellSize       = cellSize;
            _oversizeFactor = oversizeFactor;
            Grid           = new SpatialGrid(cellSize, oversizeFactor);
            _builtRevision  = -1;
        }

        if (scene.Revision != _builtRevision)
        {
            Grid.Rebuild(scene.Entities);
            _builtRevision = scene.Revision;
        }
        
        _visible.Clear();
        if (enabled)
            Grid.Cull(frustum, _visible);
    }

    // When culling is disabled every entity counts as visible.
    public bool IsVisible(Entity entity) => !_enabled || _visible.Contains(entity);
    
    public void CullInto(Frustum frustum, HashSet<Entity> results) =>
        Grid.Cull(frustum, results, markVisited: false);
}