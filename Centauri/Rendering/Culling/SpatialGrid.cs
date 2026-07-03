namespace Centauri.Rendering.Culling;

using System.Numerics;

using World;
using Utils.Geometry;

public sealed class SpatialGrid
{
    private readonly float _cellSize;
    private readonly float _oversizeExtent;   // wider than this in X or Z → bypass the grid

    private List<Entity>[] _cells = [];
    private bool[]         _visited = [];      // parallel to _cells: touched by the last marking Cull
    private readonly List<Entity> _oversized = [];
    // Non-empty cell indices, rebuilt alongside _cells — cell contents only ever change during
    // Rebuild(), so Cull() (called once for the main camera and once per shadow cascade between
    // rebuilds) can walk just the occupied cells instead of the full Rows x Columns rectangle.
    private readonly List<int> _occupiedIndices = [];

    private Vector3 _origin;                    // min corner (minX, minY, minZ)
    private float   _maxY;
    
    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public int EntityCount { get; private set; }
    
    public int OccupiedCells { get; private set; }

    public int VisitedCells { get; private set; }

    public SpatialGrid(float cellSize = 16f, float oversizeFactor = 8f)
    {
        _cellSize       = cellSize;
        _oversizeExtent = cellSize * oversizeFactor;
    }

    public void Rebuild(IReadOnlyList<Entity> entities)
    {
        _oversized.Clear();
        EntityCount  = 0;
        OccupiedCells = 0;

        // 1) classify into oversized vs grid-resident, and find the resident XZ/Y extent
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var anyResident = false;

        foreach (var e in entities)
        {
            if (!e.Enabled || e.Model is null) continue;
            
            var b = e.GetWorldBounds();
            EntityCount++;

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
            Columns = Rows = 0;
            _cells   = [];
            _visited = [];
            _occupiedIndices.Clear();
            _origin  = Vector3.Zero;
            _maxY    = 0f;
            return;
        }

        _origin  = new Vector3(min.X, min.Y, min.Z);
        _maxY    = max.Y;
        Columns = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / _cellSize));
        Rows    = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / _cellSize));

        AllocateCells(Columns * Rows);

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
                    _cells[r * Columns + c].Add(e);
        }
        
        _occupiedIndices.Clear();
        for (var i = 0; i < _cells.Length; i++)
            if (_cells[i].Count > 0)
                _occupiedIndices.Add(i);
        OccupiedCells = _occupiedIndices.Count;
    }

    // Gather every entity whose bounds pass the frustum into `results` (deduped). Records the
    // cells it visited so a debug overlay can show the query footprint.
    public void Cull(Frustum frustum, HashSet<Entity> results, bool markVisited = true)
    {
        if (markVisited && _visited.Length > 0)
        {
            Array.Clear(_visited);
            VisitedCells = 0;
        }

        foreach (var e in _oversized)
            if (frustum.IsVisibleAABB(e.GetWorldBounds()))
                results.Add(e);

        foreach (var idx in _occupiedIndices)
        {
            var c = idx % Columns;
            var r = idx / Columns;

            if (!frustum.IsVisibleAABB(CellBounds(c, r))) continue;

            if (markVisited)
            {
                _visited[idx] = true;
                VisitedCells++;
            }

            foreach (var e in _cells[idx])
                if (frustum.IsVisibleAABB(e.GetWorldBounds()))
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

    public int  CellCount  (int col, int row) => _cells[row * Columns + col].Count;
    public bool CellVisited(int col, int row) => _visited.Length > 0 && _visited[row * Columns + col];
    
    public bool TryGetCells(BoundingBox b, out int c0, out int r0, out int c1, out int r1)
    {
        c0 = r0 = c1 = r1 = 0;
        if (Columns == 0 || Rows == 0 || IsOversized(b)) return false;

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
        return (Math.Clamp(c, 0, Columns - 1), Math.Clamp(r, 0, Rows - 1));
    }
}