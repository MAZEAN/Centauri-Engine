namespace Centauri.Rendering.Helper;

using System.Runtime.CompilerServices;

using Graphics.Resources.Materials;
using Graphics.Geometry;
using World;

// Identity-based key over a material array/list: two entities batch together only if their
// material slots are the *same* Material references, in the same order. Wraps whatever list
// it's given without copying, so it's cheap to build transiently for a Dictionary lookup — but
// the key actually stored in a Dictionary must wrap an immutable snapshot (Batch.Materials),
// never an entity's live (mutable-in-place, via MakeMaterialUnique) materials list.
internal readonly struct MaterialArrayKey : IEquatable<MaterialArrayKey>
{
    private readonly IReadOnlyList<Material?> _materials;

    public MaterialArrayKey(IReadOnlyList<Material?> materials) => _materials = materials;

    public bool Equals(MaterialArrayKey other)
    {
        if (_materials.Count != other._materials.Count) return false;
        for (var i = 0; i < _materials.Count; i++)
            if (!ReferenceEquals(_materials[i], other._materials[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is MaterialArrayKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var m in _materials)
            hash.Add(m is null ? 0 : RuntimeHelpers.GetHashCode(m));
        return hash.ToHashCode();
    }
}

public sealed class Batch
{
    public Batch(Model model, Material?[] materials)
    {
        Model    = model;
        Materials = materials;
    }

    public Model       Model     { get; }
    public Material?[] Materials { get; }
    public List<Entity> Entities { get; } = [];
}

public sealed class ShaderBatcher
{
    private readonly List<Batch> _batches = [];
    private int _revision = -1;
    
    public int RenderableEntities { get; private set; }
    public int TwoSidedEntities   { get; private set; }

    public IReadOnlyList<Batch> GetBatches(Scene scene)
    {
        if (scene.Revision == _revision)
            return _batches;

        _batches.Clear();
        var byModel = new Dictionary<Model, Dictionary<MaterialArrayKey, Batch>>();

        foreach (var entity in scene.Entities)
        {
            if (entity.Model is not { } model) continue;   // light-only / mesh-less
            if (entity.Materials.Count == 0)    continue;

            if (!byModel.TryGetValue(model, out var perModel))
                byModel[model] = perModel = new Dictionary<MaterialArrayKey, Batch>();

            // Transient key over the entity's live list, just for this lookup.
            if (!perModel.TryGetValue(new MaterialArrayKey(entity.Materials), out var batch))
            {
                batch = new Batch(model, entity.Materials.ToArray());
                // Stored key wraps the batch's own immutable snapshot, not the entity's list.
                perModel[new MaterialArrayKey(batch.Materials)] = batch;
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

    private static ulong SortKey(Batch b) =>
        b.Materials.Length > 0 && b.Materials[0] is { } m ? m.SortKey : 0;
}