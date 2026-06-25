namespace Centauri.Rendering.Helper;

using Graphics.Resources;
using Graphics.Resources.Materials;
using Graphics.Geometry;
using World;

public sealed class Batch
{
    public Batch(Model model, Material material)
    {
        Model    = model;
        Material = material;
    }

    public Model    Model    { get; }
    public Material Material { get; }
    public List<Entity> Entities { get; } = new();
}

// Groups scene entities for instanced drawing: first by shader, then coalesced by
// (model, material) so every entity sharing all three is drawn in one instanced call per
// mesh. Within a shader, batches are sorted by material so consecutive draws reuse texture
// binds. Rebuilt only when the scene changes (tracked by Scene.Revision).
public sealed class ShaderBatcher
{
    // A run of entities sharing shader + model + material → one instanced draw per mesh.
    private readonly Dictionary<GLShader, List<Batch>> _groups = new();
    private int _revision = -1;

    public IReadOnlyDictionary<GLShader, List<Batch>> GetGroups(Scene scene)
    {
        if (scene.Revision == _revision)
            return _groups;

        _groups.Clear();
        
        // (shader) -> (model, material) -> batch, so identical instances coalesce. Models
        // and materials are cached/shared, so reference equality groups them correctly.
        var index = new Dictionary<GLShader, Dictionary<(Model, Material), Batch>>();

        foreach (var entity in scene.Entities)
        {
            if (entity.Material is not { } material) continue;   // light-only / mesh-less
            if (entity.Model    is not { } model)    continue;
            
            var shader = material.Shader;
            if (!index.TryGetValue(shader, out var byKey))
            {
                byKey           = new Dictionary<(Model, Material), Batch>();
                index[shader]   = byKey;
                _groups[shader] = new List<Batch>();
            }

            var key = (model, material);
            if (!byKey.TryGetValue(key, out var batch))
            {
                batch = new Batch(model, material);
                byKey[key] = batch;
                _groups[shader].Add(batch);
            }

            batch.Entities.Add(entity);
        }

        // sort each group by material so texture binds are minimized
        foreach (var batches in _groups.Values)
            batches.Sort((a, b) => a.Material.SortKey.CompareTo(b.Material.SortKey));

        _revision = scene.Revision;
        return _groups;
    }
}