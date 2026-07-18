namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using System.Numerics;

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
    private readonly MaterialRegistry _materialRegistry;
    private readonly ModelRegistry _modelRegistry;

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
        _materialRegistry = new MaterialRegistry();
        _modelRegistry = new ModelRegistry();
    }

    public Model GetModel(string idOrPath) => Models.Get(_modelRegistry.ResolvePath(idOrPath));

    public ModelDefinition? GetModelDefinition(string idOrPath) => _modelRegistry.GetDefinition(idOrPath);

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
    // one to place (the Outliner's "add entity" flow, the Inspector's per-slot material picker)
    // rather than typing a path/id by hand.
    public IEnumerable<string> ModelIds    => _modelRegistry.Ids;
    public IEnumerable<string> MaterialIds => _materialRegistry.Ids;

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
            .Select(e => _modelRegistry.ResolvePath(e.Model!))
            .Distinct()
            .ToList();

        var materialPaths = list.SelectMany(EntityMaterialPaths).Distinct();
        var texturePaths = new HashSet<string>();

        foreach (var matPath in materialPaths)
        {
            var m = _materialRegistry.ReadDefinition(matPath);
            AddPath(texturePaths, AlbedoKey(m.Albedo, m.Opacity));
            AddPath(texturePaths, m.Normal);
            AddPath(texturePaths, m.Roughness);
            AddPath(texturePaths, m.Metallic);
            AddPath(texturePaths, m.AO);
            AddPath(texturePaths, m.Height);
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

    private Material LoadMaterial(string path)
    {
        var def = _materialRegistry.ReadDefinition(path);

        var shader = Shaders.Get(def.Shader);

        return new Material(shader)
        {
            Name      = def.Name,
            Albedo    = AlbedoKey(def.Albedo, def.Opacity) is { } albedoKey ? Textures.Get(albedoKey) : null,
            Normal    = def.Normal    != null ? Textures.Get(def.Normal)    : null,
            Roughness = def.Roughness != null ? Textures.Get(def.Roughness) : null,
            Metallic  = def.Metallic  != null ? Textures.Get(def.Metallic)  : null,
            AO        = def.AO        != null ? Textures.Get(def.AO)         : DefaultTexture,
            Height    = def.Height    != null ? Textures.Get(def.Height)    : null,
            RoughnessScalar = def.RoughnessScalar,
            MetallicScalar  = def.MetallicScalar,
            Translucency   = def.TranslucencyScalar,
            Color          = new Vector4(def.Color[0], def.Color[1], def.Color[2], def.Color[3]),
            TwoSided       = def.TwoSided,
            Wind           = def.Wind,
            Triplanar      = def.Triplanar,
            TriplanarScale = def.TriplanarScale,
            ParallaxScale  = def.ParallaxScale,
            ParallaxEnabled = def.ParallaxEnabled,
            UvScale  = def.UvScale  is { Length: 2 } s ? new Vector2(s[0], s[1]) : Vector2.One,
            UvOffset = def.UvOffset is { Length: 2 } o ? new Vector2(o[0], o[1]) : Vector2.Zero,
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
