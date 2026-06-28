namespace Centauri.Rendering.Culling;

using System.Numerics;

using World;
using Utils.Geometry;

// Uniform grid over the scene's XZ extent — the natural broad-phase for forests, meadows and
// terrain, where content spreads horizontally. Each cell is a column spanning the full scene
// height. Entities far wider than a cell (ground / terrain planes) bypass the grid and are
// always tested, so one huge object doesn't smear across every cell and inflate the bounds.
//
// Built to be inspectable: cell bounds, occupancy and the cells touched by the last query are
// all exposed so a debug overlay can draw the structure and the query footprint.
public sealed class SpatialGrid
{
    private readonly float _cellSize;
    private readonly float _oversizeFactor;
    private readonly float _oversizeExtent;   // wider than this in X or Z → bypass the grid

    private List<Entity>[] _cells = [];
    private bool[]         _visited = [];      // parallel to _cells: touched by the last marking Cull
    private readonly List<Entity> _oversized = new();

    private int     _columns;
    private int     _rows;
    private Vector3 _origin;                    // min corner (minX, minY, minZ)
    private float   _maxY;

    private int _entityCount;                   // grid-resident + oversized (unique)
    private int _occupiedCells;
    private int _visitedCells;                  // cells hit by the last marking query

    public float   CellSize       => _cellSize;
    public float   OversizeFactor => _oversizeFactor;
    public int     Columns        => _columns;
    public int     Rows           => _rows;
    public Vector3 Origin         => _origin;
    public float   MinY           => _origin.Y;
    public float   MaxY           => _maxY;

    public int EntityCount   => _entityCount;
    public int CellCountTotal => _columns * _rows;
    public int OccupiedCells  => _occupiedCells;
    public int VisitedCells   => _visitedCells;
    public IReadOnlyList<Entity> Oversized => _oversized;

    public SpatialGrid(float cellSize = 16f, float oversizeFactor = 8f)
    {
        _cellSize       = cellSize;
        _oversizeFactor = oversizeFactor;
        _oversizeExtent = cellSize * oversizeFactor;
    }

    public void Rebuild(IReadOnlyList<Entity> entities)
    {
        _oversized.Clear();
        _entityCount  = 0;
        _occupiedCells = 0;

        // 1) classify into oversized vs grid-resident, and find the resident XZ/Y extent
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var anyResident = false;

        foreach (var e in entities)
        {
            if (!e.Enabled || e.Model is null) continue;
            var b = e.GetWorldBounds();
            _entityCount++;

            if (IsOversized(b))
            {
                _oversized.Add(e);
                min.Y = MathF.Min(min.Y, b.Min.Y);   // columns still span oversized heights
                max.Y = MathF.Max(max.Y, b.Max.Y);
                continue;
            }

            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
            anyResident = true;
        }

        if (!anyResident)   // only oversized entities (or an empty scene)
        {
            _columns = _rows = 0;
            _cells   = [];
            _visited = [];
            _origin  = Vector3.Zero;
            _maxY    = 0f;
            return;
        }

        _origin  = new Vector3(min.X, min.Y, min.Z);
        _maxY    = max.Y;
        _columns = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / _cellSize));
        _rows    = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / _cellSize));

        AllocateCells(_columns * _rows);

        // 2) insert each grid-resident entity into every cell its XZ footprint covers
        foreach (var e in entities)
        {
            if (!e.Enabled || e.Model is null) continue;
            var b = e.GetWorldBounds();
            if (IsOversized(b)) continue;

            var (c0, r0) = CellOf(b.Min);
            var (c1, r1) = CellOf(b.Max);
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    _cells[r * _columns + c].Add(e);
        }
        
        foreach (var cell in _cells)
            if (cell.Count > 0) 
                _occupiedCells++;
    }

    // Gather every entity whose bounds pass the frustum into `results` (deduped). Records the
    // cells it visited so a debug overlay can show the query footprint.
    public void Cull(Frustum frustum, HashSet<Entity> results, bool markVisited = true)
    {
        if (markVisited && _visited.Length > 0)
        {
            Array.Clear(_visited);
            _visitedCells = 0;
        }

        foreach (var e in _oversized)
            if (frustum.IsVisibleAABB(e.GetWorldBounds()))
                results.Add(e);

        for (var r = 0; r < _rows; r++)
            for (var c = 0; c < _columns; c++)
            {
                var idx  = r * _columns + c;
                var cell = _cells[idx];
                
                if (cell.Count == 0) continue;
                if (!frustum.IsVisibleAABB(CellBounds(c, r))) continue;

                if (markVisited)
                {
                    _visited[idx] = true;
                    _visitedCells++;
                }
                
                foreach (var e in cell)
                    if (!results.Contains(e) && frustum.IsVisibleAABB(e.GetWorldBounds()))
                        results.Add(e);
            }
    }

    // ── debug-overlay accessors ─────────────────────────────────────────────────
    public BoundingBox CellBounds(int col, int row)
    {
        var minX = _origin.X + col * _cellSize;
        var minZ = _origin.Z + row * _cellSize;
        return new BoundingBox(
            new Vector3(minX, _origin.Y, minZ),
            new Vector3(minX + _cellSize, _maxY, minZ + _cellSize));
    }

    public int  CellCount  (int col, int row) => _cells[row * _columns + col].Count;
    public bool CellVisited(int col, int row) => _visited.Length > 0 && _visited[row * _columns + col];
    
    public bool TryGetCells(BoundingBox b, out int c0, out int r0, out int c1, out int r1)
    {
        c0 = r0 = c1 = r1 = 0;
        if (_columns == 0 || _rows == 0 || IsOversized(b)) return false;

        (c0, r0) = CellOf(b.Min);
        (c1, r1) = CellOf(b.Max);
        return true;
    }

    // ── internals ───────────────────────────────────────────────────────────────
    private bool IsOversized(BoundingBox b) =>
        b.Max.X - b.Min.X > _oversizeExtent || b.Max.Z - b.Min.Z > _oversizeExtent;

    private void AllocateCells(int count)
    {
        if (_cells.Length == count)
        {
            foreach (var cell in _cells) 
                cell.Clear();
            return;
        }

        _cells   = new List<Entity>[count];
        _visited = new bool[count];
        for (var i = 0; i < count; i++) 
            _cells[i] = new List<Entity>();
    }

    private (int col, int row) CellOf(Vector3 p)
    {
        var c = (int)((p.X - _origin.X) / _cellSize);
        var r = (int)((p.Z - _origin.Z) / _cellSize);
        return (Math.Clamp(c, 0, _columns - 1), Math.Clamp(r, 0, _rows - 1));
    }
}
