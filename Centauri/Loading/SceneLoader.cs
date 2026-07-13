namespace Centauri.Loading;

using System.Text.Json;
using System.Numerics;

using Graphics.Resources.Materials;
using Graphics.Geometry;
using Config;
using Rendering;
using Utils.Misc;
using World;

public class SceneLoader
{
    private readonly ResourceSystem _resourceSystem;
    private readonly Scene _scene;
    private readonly AppConfig _config;

    private readonly string _path;

    // The merged definition from the last Load(), and which EntityDefinition each live Entity
    // came from — Save() reuses both so it only ever writes back the fields the inspector can
    // actually edit (name, enabled, transform, light) and leaves everything else (model,
    // material bindings, uv, components, triplanar overrides) exactly as originally authored.
    private SceneDefinition? _loaded;
    private readonly Dictionary<Entity, EntityDefinition> _sources = new();

    public SceneLoader(ResourceSystem resourceSystem, Scene scene, AppConfig config)
    {
        _resourceSystem = resourceSystem;
        _scene = scene;
        _path = config.Render.ScenePath;
        _config = config;
    }

    public void Load()
    {
        var def = LoadMerged(_path, []);
        _loaded = def;

        _resourceSystem.PreloadScene(def);

        LoadEntities(def);
        LoadCameras(def);
        LoadSkyboxes(def);
    }

    // Multi-file ("include") scenes flatten every included file's entities into one in-memory
    // list with no record of which file an entity came from, so Save() has no correct place to
    // write each one back to — refuse rather than silently collapsing the project into one file.
    public bool CanSave => _loaded is { Include: not { Count: > 0 } };

    // Writes the scene's live, editor-authored state back to the file it was loaded from.
    // Cameras and skybox exposure/black level aren't persisted yet — only entities, which is
    // what the inspector's Transform/Enabled/Light editing (and Authored-value tracking) covers.
    public void Save()
    {
        if (_loaded is not { } loaded)
            throw new InvalidOperationException("SceneLoader.Save() called before Load().");
        if (loaded.Include is { Count: > 0 })
            throw new InvalidOperationException("Saving a scene that uses \"include\" is not supported yet.");

        var outDef = new SceneDefinition
        {
            Entities = _scene.Entities.Select(ToDefinition).ToList(),
            Cameras  = loaded.Cameras,
            Skyboxes = loaded.Skyboxes,
        };

        var json = JsonSerializer.Serialize(outDef, JsonDefaults.Options);
        File.WriteAllText(PathResolver.Resolve(_path), json);
    }

    private EntityDefinition ToDefinition(Entity entity)
    {
        if (!_sources.TryGetValue(entity, out var source))
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' has no source definition (not created by SceneLoader) — cannot save.");

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

    // Recursively resolves "include": each included file's entities/cameras/skybox get folded
    // into this one before the caller sees it, so a scene can stay a thin index instead of one
    // file holding everything as content grows. Optional — a single-file scene with no
    // "include" behaves exactly as before.
    private static SceneDefinition LoadMerged(string path, HashSet<string> visiting)
    {
        var fullPath = PathResolver.Resolve(path);
        if (!visiting.Add(fullPath))
            throw new Exception($"Scene include cycle detected involving '{path}'.");

        var json = File.ReadAllText(fullPath);
        var def  = JsonSerializer.Deserialize<SceneDefinition>(json, JsonDefaults.Options)
                   ?? throw new Exception($"Failed to deserialize scene file: {path}");

        if (def.Include is not { Count: > 0 } includes)
            return def;

        foreach (var included in includes.Select(inc => LoadMerged(inc, visiting)))
        {
            def.Entities.AddRange(included.Entities);
            def.Cameras.AddRange(included.Cameras);
            def.Skyboxes.AddRange(included.Skyboxes);
        }

        return def;
    }

    private void LoadEntities(SceneDefinition def)
    {
        foreach (var e in def.Entities)
        {
            var entity = BuildEntity(e);
            ApplyTransform(entity, e);
            ApplyComponents(entity, e);

            _scene.AddEntity(entity);
            _sources[entity] = e;
        }
    }
    
    private Entity BuildEntity(EntityDefinition e)
    {
        var model = !string.IsNullOrEmpty(e.Model)
            ? _resourceSystem.GetModel(e.Model)
            : null;
        
        var materials = ResolveMaterials(e, model);
        var light = e.Light is { } l ? CreateLight(l) : null;
        
        var entity = new Entity(model, materials, light)
        {
            Name    = e.Name,
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

    private void LoadCameras(SceneDefinition def)
    {
        if (def.Cameras.Count == 0)
            throw new Exception("Scene must contain at least one camera.");

        foreach (var c in def.Cameras)
        {
            var camera = new Camera(
                _config.Camera,
                c.Name,
                new Vector3(c.Position[0], c.Position[1], c.Position[2]),
                ParseUp(c.Up),
                c.Yaw,
                c.Pitch
            );

            _scene.Cameras.Add(camera);
        }

        // active (view) camera — honor the scene's `active` flag, fall back to the first
        var active = def.Cameras.FirstOrDefault(c => c.Active) ?? def.Cameras[0];
        _scene.Cameras.SetActive(active.Name);

        // primary (culling) camera — honor `primary`, fall back to the active camera
        var primary = def.Cameras.FirstOrDefault(c => c.Primary) ?? active;
        _scene.Cameras.SetPrimary(primary.Name);
    }
    
    private void LoadSkyboxes(SceneDefinition def)
    {
        foreach (var s in def.Skyboxes)
            if (s.Panorama.Length > 0)
                _scene.Skyboxes.Add(s.Name, _resourceSystem.Textures.Get(s.Panorama), s.Exposure, s.BlackLevel);

        // honor an `active` flag like cameras do; otherwise the first stays active
        var active = def.Skyboxes.FirstOrDefault(s => s.Active);
        if (active is { Name.Length: > 0 })
            _scene.Skyboxes.SetActive(active.Name);
    }
    
    private static Vector3 ParseUp(string axis)
    {
        return axis.ToUpper() switch
        {
            "X" => Vector3.UnitX,
            "Y" => Vector3.UnitY,
            "Z" => Vector3.UnitZ,
            _ => throw new Exception($"Invalid up axis: {axis}")
        };
    }
}