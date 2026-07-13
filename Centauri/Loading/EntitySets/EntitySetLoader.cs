namespace Centauri.Loading;

using System.Text.Json;
using System.Numerics;

using Graphics.Resources.Materials;
using Graphics.Geometry;
using Config;
using Rendering;
using Utils.Misc;
using World;

// Loads zero or more EntitySetDefinition files (AppConfig's Render.EntitySetPaths) into the
// scene, and can write them back out. Each file
// keeps its own identity end to end: every live Entity remembers both the EntityDefinition it
// was built from and which file that came from (_sources / _fileOf), so Save() always has an
// unambiguous, correct destination for it — including entities added at runtime via
// CreateEntity(), which are attributed to Render.DefaultEntitySetPath until saved once, at
// which point that file starts existing on disk like any other set.
public class EntitySetLoader
{
    private readonly ResourceSystem _resourceSystem;
    private readonly Scene _scene;
    private readonly AppConfig _config;

    private readonly Dictionary<Entity, EntityDefinition> _sources = new();
    private readonly Dictionary<Entity, string> _fileOf = new();

    public EntitySetLoader(ResourceSystem resourceSystem, Scene scene, AppConfig config)
    {
        _resourceSystem = resourceSystem;
        _scene = scene;
        _config = config;
    }

    // Loads every configured entity set (Render.EntitySetPaths), in order. An empty list is
    // valid and expected — the default is an empty scene (environment only); entity content is
    // opt-in via config, or added live via CreateEntity().
    public void LoadAll()
    {
        var paths = _config.Render.EntitySetPaths;
        if (paths.Count == 0) return;

        var definitions = paths.Select(p => (path: p, def: LoadDefinition(p))).ToList();

        _resourceSystem.PreloadEntities(definitions.SelectMany(d => d.def.Entities));

        foreach (var (path, def) in definitions)
            foreach (var e in def.Entities)
                AddFromDefinition(e, path);
    }

    private static EntitySetDefinition LoadDefinition(string path)
    {
        var fullPath = PathResolver.Resolve(path);
        var json = File.ReadAllText(fullPath);
        return JsonSerializer.Deserialize<EntitySetDefinition>(json, JsonDefaults.Options)
               ?? throw new Exception($"Failed to deserialize entity set file: {path}");
    }

    private void AddFromDefinition(EntityDefinition e, string sourcePath)
    {
        var entity = BuildEntity(e);
        ApplyTransform(entity, e);
        ApplyComponents(entity, e);

        _scene.AddEntity(entity);
        _sources[entity] = e;
        _fileOf[entity]  = sourcePath;
    }

    // Adds a brand-new entity (no prior EntityDefinition) placing the given model id — the
    // "compose a new entity from the available object list" workflow. materialId is optional;
    // when omitted the usual resolution chain applies (the model's own default binding, else
    // DefaultMaterial). Attributed to Render.DefaultEntitySetPath so Save() has somewhere to put
    // it; that file doesn't need to already exist.
    public Entity CreateEntity(string? modelId, string? materialId = null, string name = "New Entity")
    {
        var def = new EntityDefinition
        {
            Name     = name,
            Model    = modelId,
            Material = materialId,
        };

        var entity = BuildEntity(def);
        ApplyTransform(entity, def);

        _scene.AddEntity(entity);
        _sources[entity] = def;
        _fileOf[entity]  = _config.Render.DefaultEntitySetPath;

        return entity;
    }

    // Removes an entity the editor created/loaded — drops its save tracking too, so a deleted
    // entity doesn't reappear on the next Save() of whichever file it belonged to.
    public void DeleteEntity(Entity entity)
    {
        _scene.RemoveEntity(entity);
        _sources.Remove(entity);
        _fileOf.Remove(entity);
    }

    // Writes every tracked entity back to the file it's attributed to (grouping by file), one
    // EntitySetDefinition per file — composing several sets together at load time never
    // collapses them into one on save. Entities without tracking (shouldn't happen outside a
    // bug) are skipped rather than silently dropped from an arbitrary file.
    public void Save()
    {
        var byFile = _scene.Entities
            .Where(e => _fileOf.ContainsKey(e))
            .GroupBy(e => _fileOf[e]);

        foreach (var group in byFile)
        {
            var outDef = new EntitySetDefinition { Entities = group.Select(ToDefinition).ToList() };
            var json = JsonSerializer.Serialize(outDef, JsonDefaults.Options);
            File.WriteAllText(PathResolver.Resolve(group.Key), json);
        }
    }

    private EntityDefinition ToDefinition(Entity entity)
    {
        var source = _sources[entity];
        var t = entity.Transform;

        return new EntityDefinition
        {
            Name      = entity.Name,
            Model     = source.Model,
            Material  = source.Material,
            Materials = source.Materials,
            Position  = [t.Position.X, t.Position.Y, t.Position.Z],
            Scale     = [t.Scale.X, t.Scale.Y, t.Scale.Z],
            Rotation  = [t.EulerAngles.X, t.EulerAngles.Y, t.EulerAngles.Z],
            UvScale   = source.UvScale,
            UvOffset  = source.UvOffset,
            Enabled   = entity.Enabled,
            Light     = entity.Light is { } l ? ToDefinition(l) : null,
            Components = source.Components,
            TriplanarOverride      = source.TriplanarOverride,
            TriplanarScaleOverride = source.TriplanarScaleOverride,
        };
    }

    private static LightDefinition ToDefinition(Light l) => l switch
    {
        DirectionalLight d => new LightDefinition
        {
            Type = "directional", Enabled = d.Enabled,
            Color = [d.Color.X, d.Color.Y, d.Color.Z], Intensity = d.Intensity,
            Direction = [d.Direction.X, d.Direction.Y, d.Direction.Z],
        },
        SpotLight sp => new LightDefinition
        {
            Type = "spot", Enabled = sp.Enabled,
            Color = [sp.Color.X, sp.Color.Y, sp.Color.Z], Intensity = sp.Intensity,
            Direction = [sp.Direction.X, sp.Direction.Y, sp.Direction.Z],
            InnerCutoff = sp.InnerCutoff, OuterCutoff = sp.OuterCutoff,
        },
        PointLight p => new LightDefinition
        {
            Type = "point", Enabled = p.Enabled,
            Color = [p.Color.X, p.Color.Y, p.Color.Z], Intensity = p.Intensity,
            Constant = p.Constant, Linear = p.Linear, Quadratic = p.Quadratic,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(l), l, "Unknown light type.")
    };

    private Entity BuildEntity(EntityDefinition e)
    {
        var model = !string.IsNullOrEmpty(e.Model)
            ? _resourceSystem.GetModel(e.Model)
            : null;

        var materials = ResolveMaterials(e, model);
        var light = e.Light is { } l ? CreateLight(l) : null;

        var entity = new Entity(model, materials, light)
        {
            Name     = e.Name,
            UvScale  = new Vector2(e.UvScale[0],  e.UvScale[1]),
            UvOffset = new Vector2(e.UvOffset[0], e.UvOffset[1]),
            Enabled  = e.Enabled
        };
        return entity;
    }

    private static void ApplyTransform(Entity entity, EntityDefinition e)
    {
        entity.Transform.Position = new Vector3(e.Position[0], e.Position[1], e.Position[2]);
        entity.Transform.Scale    = new Vector3(e.Scale[0],    e.Scale[1],    e.Scale[2]);

        if (e.Rotation is { Length: 3 })
            entity.Transform.SetEulerAngles(e.Rotation[0], e.Rotation[1], e.Rotation[2]);

        entity.Authored = new TransformSnapshot(
            entity.Transform.Position,
            entity.Transform.EulerAngles,
            entity.Transform.Scale
        );
    }

    private static void ApplyComponents(Entity entity, EntityDefinition e)
    {
        if (e.Components is not { Count: > 0 }) return;

        foreach (var c in e.Components)
            entity.AddComponent(ComponentFactory.Create(c));
    }

    private Material?[] ResolveMaterials(EntityDefinition e, Model? model)
    {
        if (model is null)
            return Array.Empty<Material?>();

        var count = model.Meshes.Count;
        var result = new Material?[count];

        // Priority: the entity's own binding, else its singular "material", else whatever the
        // placed model declares as its default (Assets/Objects/**/*.model) — so placing the
        // same model repeatedly doesn't require repeating its material list every time.
        var modelDef = !string.IsNullOrEmpty(e.Model) ? _resourceSystem.GetModelDefinition(e.Model) : null;
        var binding = e.Materials
                      ?? (!string.IsNullOrEmpty(e.Material) ? new MaterialBinding { Indexed = [e.Material!] } : null)
                      ?? modelDef?.Materials;

        // Overrides the resolved material's own triplanar setting for this entity, entity value
        // taking priority over the model's default. Rarely set — see EntityDefinition's comment.
        var triplanar      = e.TriplanarOverride      ?? modelDef?.TriplanarOverride;
        var triplanarScale = e.TriplanarScaleOverride ?? modelDef?.TriplanarScaleOverride;

        Material Resolve(string path)
        {
            var mat = _resourceSystem.GetMaterial(path);
            if (triplanar is null && triplanarScale is null)
                return mat;

            var overridden = mat.Clone();
            if (triplanar is { } t) overridden.Triplanar = t;
            if (triplanarScale is { } s) overridden.TriplanarScale = s;
            return overridden;
        }

        // Named binding: matched to each mesh's name from the model file, not array position —
        // no need to know/verify mesh order inside the exported model.
        if (binding?.Named is { Count: > 0 } named)
        {
            for (var i = 0; i < count; i++)
            {
                var meshName = model.Meshes[i].Name;
                result[i] = !string.IsNullOrEmpty(meshName) && named.TryGetValue(meshName, out var path)
                    ? Resolve(path)
                    : _resourceSystem.DefaultMaterial;
            }
            return result;
        }

        var paths = binding?.Indexed is { Length: > 0 } indexed ? indexed : null;

        for (var i = 0; i < count; i++)
            result[i] = paths is null
                ? _resourceSystem.DefaultMaterial
                : Resolve(paths[Math.Min(i, paths.Length - 1)]);

        return result;
    }

    private static Light CreateLight(LightDefinition l)
    {
        var color     = new Vector3(l.Color[0], l.Color[1], l.Color[2]);
        var direction = new Vector3(l.Direction[0], l.Direction[1], l.Direction[2]);

        return l.Type.ToLowerInvariant() switch
        {
            "directional" => new DirectionalLight
            {
                Color = color, Intensity = l.Intensity, Enabled = l.Enabled,
                Direction = direction
            },
            "spot" => new SpotLight
            {
                Color = color, Intensity = l.Intensity, Enabled = l.Enabled,
                Direction = direction,
                InnerCutoff = l.InnerCutoff, OuterCutoff = l.OuterCutoff
            },
            "point" => new PointLight
            {
                Color = color, Intensity = l.Intensity, Enabled = l.Enabled,
                Constant = l.Constant, Linear = l.Linear, Quadratic = l.Quadratic
            },
            _ => throw new Exception($"Unknown light type '{l.Type}'.")
        };
    }
}
