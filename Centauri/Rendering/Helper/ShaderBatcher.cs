namespace Centauri.Rendering.Helper;

using Graphics.Resources.Materials;
using Graphics.Geometry;
using World;

public sealed class Batch
{
    public Batch(Model model, Material?[] materials)
    {
        Model    = model;
        Materials = materials;
    }

    public Model       Model     { get; }
    public Material?[] Materials { get; }
    public List<Entity> Entities { get; } = new();
}

public sealed class ShaderBatcher
{
    private readonly List<Batch> _batches = new();
    private int _revision = -1;
    
    public int RenderableEntities { get; private set; }
    public int TwoSidedEntities   { get; private set; }

    public IReadOnlyList<Batch> GetBatches(Scene scene)
    {
        if (scene.Revision == _revision)
            return _batches;

        _batches.Clear();
        var byModel = new Dictionary<Model, List<Batch>>();

        foreach (var entity in scene.Entities)
        {
            if (entity.Model is not { } model) continue;   // light-only / mesh-less
            if (entity.Materials.Count == 0)    continue;
            
            if (!byModel.TryGetValue(model, out var perModel))
                byModel[model] = perModel = new List<Batch>();

            var batch = Find(perModel, entity);
            if (batch is null)
            {
                batch = new Batch(model, entity.Materials.ToArray());
                perModel.Add(batch);
                _batches.Add(batch);
            }

            batch.Entities.Add(entity);
        }
        
        RenderableEntities = 0;
        TwoSidedEntities   = 0;
        foreach (var batch in _batches)
        {
            RenderableEntities += batch.Entities.Count;
            if (batch.Entities[0].AnyTwoSided)
                TwoSidedEntities += batch.Entities.Count;
        }
        _batches.Sort((a, b) => SortKey(a).CompareTo(SortKey(b)));

        _revision = scene.Revision;
        return _batches;
    }
    
    private static Batch? Find(List<Batch> perModel, Entity entity)
    {
        foreach (var batch in perModel)
            if (MaterialsMatch(batch.Materials, entity.Materials))
                return batch;
        return null;
    }

    private static bool MaterialsMatch(Material?[] a, IReadOnlyList<Material?> b)
    {
        if (a.Length != b.Count) return false;
        for (var i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    private static ulong SortKey(Batch b) =>
        b.Materials.Length > 0 && b.Materials[0] is { } m ? m.SortKey : 0;
}