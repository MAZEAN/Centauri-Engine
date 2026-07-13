namespace Centauri.Loading;

using System.Text.Json;

using Config;
using Rendering;
using Utils.Misc;
using World;

// Loads zero or more EntitySetDefinition files (AppConfig's Render.EntitySetPaths, plus
// Render.DefaultEntitySetPath if it exists — see EffectivePaths) into the scene, and can write
// them back out. Each file keeps its own identity end to end: every live Entity remembers both
// the EntityDefinition it was built from and which file that came from (_sources / _fileOf), so
// Save() always has an unambiguous, correct destination for it — including entities added at
// runtime via CreateEntity(), which are attributed to Render.DefaultEntitySetPath until saved
// once, at which point that file starts existing on disk like any other set and (from then on)
// loads automatically too, without needing to be added to EntitySetPaths by hand.
public class EntitySetLoader
{
    private readonly ResourceSystem _resourceSystem;
    private readonly Scene _scene;
    private readonly AppConfig _config;
    private readonly EntityFactory _factory;

    private readonly Dictionary<Entity, EntityDefinition> _sources = new();
    private readonly Dictionary<Entity, string> _fileOf = new();

    public EntitySetLoader(ResourceSystem resourceSystem, Scene scene, AppConfig config)
    {
        _resourceSystem = resourceSystem;
        _scene = scene;
        _config = config;
        _factory = new EntityFactory(resourceSystem);
    }

    // Loads every configured entity set (Render.EntitySetPaths), in order, plus
    // Render.DefaultEntitySetPath if it exists on disk and isn't already one of them — entities
    // created live (CreateEntity) save there, and without this they'd be written correctly but
    // never loaded back, since nothing added that path to EntitySetPaths for them. An empty
    // effective list is valid and expected — the default is an empty scene (environment only);
    // entity content is opt-in via config, or added live via CreateEntity().
    public void LoadAll()
    {
        var paths = EffectivePaths();
        if (paths.Count == 0) return;

        var definitions = paths.Select(p => (path: p, def: LoadDefinition(p))).ToList();

        _resourceSystem.PreloadEntities(definitions.SelectMany(d => d.def.Entities));

        foreach (var (path, def) in definitions)
            foreach (var e in def.Entities)
                AddFromDefinition(e, path);
    }

    private List<string> EffectivePaths()
    {
        var paths = new List<string>(_config.Render.EntitySetPaths);

        var defaultPath = _config.Render.DefaultEntitySetPath;
        if (!paths.Contains(defaultPath) && File.Exists(PathResolver.Resolve(defaultPath)))
            paths.Add(defaultPath);

        return paths;
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
        var entity = _factory.Build(e);

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

        var entity = _factory.Build(def);

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
}
