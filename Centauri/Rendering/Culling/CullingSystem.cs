namespace Centauri.Rendering.Culling;

using World;
using Utils.Geometry;

// Owns the scene's spatial grid and the per-frame visible set. Updated once a frame so the
// prepass and forward pass share one frustum-cull result instead of each re-testing every
// entity. The grid is exposed for a debug overlay; the visible set drives the passes.
public sealed class CullingSystem
{
    private SpatialGrid     _grid;
    private readonly HashSet<Entity> _visible = new();
    
    private int   _builtRevision = -1;
    private bool  _enabled = true;
    private float _cellSize;
    private float _oversizeFactor;

    public SpatialGrid Grid => _grid;                       // for the debug overlay
    public IReadOnlyCollection<Entity> Visible => _visible;
    public bool Enabled => _enabled;

    public CullingSystem(float cellSize = 16f, float oversizeFactor = 8f)
    {
        _cellSize       = cellSize;
        _oversizeFactor = oversizeFactor;
        _grid           = new SpatialGrid(cellSize, oversizeFactor);
    }

    // Rebuild the grid when the scene changes (revision-gated, like the shadow bounds cache —
    // runtime transform animation must bump the revision), then cull against the camera.
    public void Update(Scene scene, Camera camera, bool enabled, float cellSize, float oversizeFactor)
    {
        _enabled = enabled;
        
        if (cellSize != _cellSize || oversizeFactor != _oversizeFactor)
        {
            _cellSize       = cellSize;
            _oversizeFactor = oversizeFactor;
            _grid           = new SpatialGrid(cellSize, oversizeFactor);
            _builtRevision  = -1;   // force a rebuild with the new cell layout
        }

        if (scene.Revision != _builtRevision)
        {
            _grid.Rebuild(scene.Entities);
            _builtRevision = scene.Revision;
        }

        camera.UpdateFrustum();

        _visible.Clear();
        if (enabled)
            _grid.Cull(camera.Frustum, _visible);
    }

    // When culling is disabled every entity counts as visible.
    public bool IsVisible(Entity entity) => !_enabled || _visible.Contains(entity);
    public void CullInto(Frustum frustum, HashSet<Entity> results) =>
        _grid.Cull(frustum, results, markVisited: false);

    public int EntityCount => _grid.EntityCount;
}