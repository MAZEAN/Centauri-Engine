namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

using Utils.Caching;
using Graphics.Resources;
using Graphics.Resources.Materials;
using Config;
using Loading;
using Utils.Misc;
using Graphics.Geometry;

public class ResourceSystem : IDisposable
{
    private const string OpacityKeyMarker = "|opacity=";
    
    private readonly GL _gl;
    private readonly AppConfig _config;
    
    public AssetCache<GLTexture> Textures { get; }
    public AssetCache<GLShader> Shaders { get; }
    public AssetCache<Model> Models { get; }
    
    private readonly Dictionary<string, Material> _materials = new();
    private readonly Dictionary<string, string> _materialRegistry;
    private readonly Dictionary<string, string> _modelRegistry;
    private readonly Dictionary<string, ModelDefinition> _modelDefinitions;
    
    public GLTexture DefaultTexture { get; private set; }

    public ResourceSystem(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;

        Textures = new AssetCache<GLTexture>(
            key => new GLTexture(gl, DecodeTextureKey(key))
        );

        Shaders = new AssetCache<GLShader>(
            shaderBase => new GLShader(gl,
                PathResolver.Resolve(shaderBase + ".vert"),
                PathResolver.Resolve(shaderBase + ".frag")));

        Models = new AssetCache<Model>(
            path => new Model(gl, PathResolver.Resolve(path)));

        DefaultTexture = CreateDefaultTexture(gl);
        _materialRegistry = BuildMaterialRegistry();
        (_modelRegistry, _modelDefinitions) = BuildModelRegistry();
    }

    // Indexes every .mat under Assets/Materials by id, so entities/materials can reference
    // "Bark" instead of "Assets/Materials/Bark.mat" — moving or reorganizing a file no longer
    // breaks every reference to it. Id defaults to the filename (sans extension); an explicit
    // "id" field inside the file overrides that. A literal path (anything containing '/') is
    // still accepted as-is, so existing scene files work unchanged.
    private Dictionary<string, string> BuildMaterialRegistry()
    {
        var registry = new Dictionary<string, string>();
        var root = PathResolver.Resolve(".");
        var materialsDir = PathResolver.Resolve("Assets/Materials");

        if (!Directory.Exists(materialsDir))
            return registry;

        foreach (var file in Directory.EnumerateFiles(materialsDir, "*.mat", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var id = Path.GetFileNameWithoutExtension(file);

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.GetString() is { Length: > 0 } explicitId)
                    id = explicitId;
            }
            catch (JsonException)
            {
                // malformed JSON — surfaces as a clear error the moment this file is actually
                // loaded (ReadMaterialDef), no need to duplicate that here
            }

            if (registry.TryGetValue(id, out var existing))
                throw new Exception($"Duplicate material id '{id}': '{existing}' and '{relative}'.");

            registry[id] = relative;
        }

        return registry;
    }

    // A literal path (contains '/') is used as-is — the escape hatch for anything not under
    // the registry, and how existing "Assets/Materials/X.mat"-style references keep working.
    // Anything else is looked up by id.
    private string ResolveMaterialPath(string idOrPath)
    {
        if (idOrPath.Contains('/'))
            return idOrPath;

        if (_materialRegistry.TryGetValue(idOrPath, out var path))
            return path;

        throw new Exception(
            $"Unknown material '{idOrPath}'. Available: {string.Join(", ", _materialRegistry.Keys.OrderBy(k => k))}");
    }
    
    // Indexes every .model under Assets/Objects by id, the same way materials are indexed —
    // "model": "Tree" instead of "Assets/Objects/Trees/Tree.glb", plus (unlike a .mat, which
    // *is* the resource) each .model file points at the actual geometry file via "path" and
    // can carry reusable defaults (default material binding, default triplanar override) for
    // every entity that places it, so a repeated placement doesn't need to repeat them.
    private (Dictionary<string, string>, Dictionary<string, ModelDefinition>) BuildModelRegistry()
    {
        var registry    = new Dictionary<string, string>();
        var definitions  = new Dictionary<string, ModelDefinition>();
        var objectsDir   = PathResolver.Resolve("Assets/Objects");

        if (!Directory.Exists(objectsDir))
            return (registry, definitions);

        foreach (var file in Directory.EnumerateFiles(objectsDir, "*.model", SearchOption.AllDirectories))
        {
            ModelDefinition def;
            try
            {
                def = JsonSerializer.Deserialize<ModelDefinition>(File.ReadAllText(file), JsonDefaults.Options)
                      ?? throw new Exception($"Failed to deserialize model file: {file}");
            }
            catch (JsonException e)
            {
                throw new Exception($"Malformed model file '{file}': {e.Message}", e);
            }

            if (string.IsNullOrEmpty(def.Path))
                throw new Exception($"Model file '{file}' is missing its required \"path\".");

            var id = string.IsNullOrEmpty(def.Id) ? Path.GetFileNameWithoutExtension(file) : def.Id;

            if (registry.TryGetValue(id, out var existing))
                throw new Exception($"Duplicate model id '{id}': '{existing}' and '{def.Path}'.");

            registry[id]    = def.Path;
            definitions[id] = def;
        }

        return (registry, definitions);
    }

    // Same convention as ResolveMaterialPath: a literal path is used as-is, anything else is
    // looked up by id.
    private string ResolveModelPath(string idOrPath)
    {
        if (idOrPath.Contains('/'))
            return idOrPath;

        if (_modelRegistry.TryGetValue(idOrPath, out var path))
            return path;

        throw new Exception(
            $"Unknown model '{idOrPath}'. Available: {string.Join(", ", _modelRegistry.Keys.OrderBy(k => k))}");
    }

    public Model GetModel(string idOrPath) => Models.Get(ResolveModelPath(idOrPath));

    // Null for a literal path or an id with no .model file (nothing to default from) — callers
    // fall back to whatever the entity itself specifies.
    public ModelDefinition? GetModelDefinition(string idOrPath) =>
        !idOrPath.Contains('/') && _modelDefinitions.TryGetValue(idOrPath, out var def) ? def : null;
    
    private static GLTexture CreateDefaultTexture(GL gl)
    {
        Span<byte> pixel = [255, 255, 255, 255];
        return new GLTexture(gl, pixel, 1, 1);
    }
    
    public Material DefaultMaterial
        => field ??= new Material(Shaders.Get(_config.Render.DefaultShader)) { AO = DefaultTexture };
    
    public Material GetMaterial(string path)
    {
        if (_materials.TryGetValue(path, out var material))
            return material;

        material = LoadMaterial(path);
        _materials[path] = material;
        return material;
    }

    // Every model/material id this project's Assets/ knows about, for UI that lets you pick
    // one to place (the Outliner's "add entity" flow) rather than typing a path/id by hand.
    public IEnumerable<string> ModelIds    => _modelRegistry.Keys.OrderBy(k => k);
    public IEnumerable<string> MaterialIds => _materialRegistry.Keys.OrderBy(k => k);

    public void PreloadEnvironment(EnvironmentDefinition def)
    {
        var texturePaths = new HashSet<string>();

        foreach (var s in def.Skyboxes)
            AddPath(texturePaths, s.Panorama);

        DecodeAssetsInParallelAndUpload([], texturePaths);
    }

    public void PreloadEntities(IEnumerable<EntityDefinition> entities)
    {
        var list = entities as IReadOnlyCollection<EntityDefinition> ?? entities.ToList();

        var modelPaths = list
            .Where(e => !string.IsNullOrEmpty(e.Model))
            .Select(e => ResolveModelPath(e.Model!))
            .Distinct()
            .ToList();

        var materialPaths = list.SelectMany(EntityMaterialPaths).Distinct();
        var texturePaths = new HashSet<string>();

        foreach (var matPath in materialPaths)
        {
            var m = ReadMaterialDef(matPath);
            AddPath(texturePaths, AlbedoKey(m.Albedo, m.Opacity));
            AddPath(texturePaths, m.Normal);
            AddPath(texturePaths, m.Roughness);
            AddPath(texturePaths, m.Metallic);
            AddPath(texturePaths, m.AO);
        }

        DecodeAssetsInParallelAndUpload(modelPaths, texturePaths);
    }

    private void DecodeAssetsInParallelAndUpload(List<string> modelPaths, HashSet<string> texturePaths)
    {
        // CPU decode off the GL thread
        var textureTask = Task.WhenAll(texturePaths.Select(key =>
            Task.Run(() => (key, data: DecodeTextureKey(key)))));

        var modelTask = Task.WhenAll(modelPaths.Select(key =>
            Task.Run(() => (key, data: Model.Decode(PathResolver.Resolve(key))))));

        Time.Run($"Parallel decode ({texturePaths.Count} textures + {modelPaths.Count} models)",
            () => Task.WaitAll(textureTask, modelTask));

        Time.Run("GL upload", () =>
        {
            foreach (var (key, data) in textureTask.Result)
                Textures.Insert(key, new GLTexture(_gl, data));

            foreach (var (key, data) in modelTask.Result)
                Models.Insert(key, new Model(_gl, data));
        });
    }
    
    // Same priority EntitySetLoader.ResolveMaterials uses: the entity's own binding, else its
    // singular "material", else whatever the placed model declares as its default.
    private IEnumerable<string> EntityMaterialPaths(EntityDefinition e)
    {
        var binding = e.Materials
                      ?? (!string.IsNullOrEmpty(e.Material) ? new MaterialBinding { Indexed = [e.Material!] } : null)
                      ?? (!string.IsNullOrEmpty(e.Model) ? GetModelDefinition(e.Model)?.Materials : null);

        if (binding?.Indexed is { Length: > 0 } indexed)
            return indexed;
        
        if (binding?.Named is { Count: > 0 } named)
            return named.Values;
        
        return [];
    }

    private static void AddPath(HashSet<string> set, string? path)
    {
        if (!string.IsNullOrEmpty(path)) 
            set.Add(path);
    }

    private static string? AlbedoKey(string? albedo, string? opacity) =>
        albedo == null ? null : opacity == null ? albedo : $"{albedo}{OpacityKeyMarker}{opacity}";

    private static TextureData DecodeTextureKey(string key)
    {
        var split = key.IndexOf(OpacityKeyMarker, StringComparison.Ordinal);
        return split < 0
            ? GLTexture.Decode(PathResolver.Resolve(key))
            : GLTexture.DecodeWithOpacity(
                PathResolver.Resolve(key[..split]),
                PathResolver.Resolve(key[(split + OpacityKeyMarker.Length)..]));
    }

    private MaterialDefinition ReadMaterialDef(string idOrPath) =>
        ResolveMaterialJson(idOrPath, [])
            .Deserialize<MaterialDefinition>(JsonDefaults.Options)
        ?? throw new Exception($"Failed to deserialize material '{idOrPath}'.");

    // Resolves "extends" by merging the parent's JSON object with this one's at the raw node
    // level — fields the child doesn't mention simply aren't in its JsonObject, so they pass
    // through from the parent untouched, no need for every MaterialDefinition field to be
    // nullable just to distinguish "not set" from "explicitly set to the default value".
    private JsonObject ResolveMaterialJson(string idOrPath, HashSet<string> visiting)
    {
        var path = ResolveMaterialPath(idOrPath);
        if (!visiting.Add(path))
            throw new Exception($"Material inheritance cycle detected involving '{idOrPath}'.");

        var json = File.ReadAllText(PathResolver.Resolve(path));
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new Exception($"Failed to parse material file: {path}");

        node.TryGetPropertyValue("extends", out var extendsNode);
        var parentId = extendsNode?.GetValue<string>();
        node.Remove("extends"); // strip regardless — never a real MaterialDefinition field

        if (string.IsNullOrEmpty(parentId))
            return node;

        var merged = ResolveMaterialJson(parentId, visiting).DeepClone()!.AsObject();
        foreach (var (key, value) in node)
            merged[key] = value?.DeepClone();

        return merged;
    }

    private Material LoadMaterial(string path)
    {
        var def = ReadMaterialDef(path);

        var shader = Shaders.Get(def.Shader);

        return new Material(shader)
        {
            Name      = def.Name,
            Albedo    = AlbedoKey(def.Albedo, def.Opacity) is { } albedoKey ? Textures.Get(albedoKey) : null,
            Normal    = def.Normal    != null ? Textures.Get(def.Normal)    : null,
            Roughness = def.Roughness != null ? Textures.Get(def.Roughness) : null,
            Metallic  = def.Metallic  != null ? Textures.Get(def.Metallic)  : null,
            AO        = def.AO        != null ? Textures.Get(def.AO)         : DefaultTexture,
            RoughnessScalar = def.RoughnessScalar,
            MetallicScalar  = def.MetallicScalar,
            Translucency   = def.TranslucencyScalar,
            Color          = new Vector4(def.Color[0], def.Color[1], def.Color[2], def.Color[3]),
            TwoSided       = def.TwoSided,
            Wind           = def.Wind,
            Triplanar      = def.Triplanar,
            TriplanarScale = def.TriplanarScale
        };
    }

    public void Dispose()
    {
        Textures.Dispose();
        Shaders.Dispose();
        Models.Dispose();
        DefaultTexture.Dispose();

        Console.WriteLine("[ResourceSystem] Disposed all resources");
    }
}