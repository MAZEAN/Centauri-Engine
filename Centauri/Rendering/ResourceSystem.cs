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

        // The factory closure (used by plain Textures.Get calls) always allows compression — only
        // called for Albedo/AO/skybox paths (see LoadMaterial/AddPathNoCompress); anything
        // precision-sensitive goes through GetTexture instead, which bypasses this factory.
        Textures = new AssetCache<GLTexture>(
            key => new GLTexture(gl, DecodeTextureKey(key, allowCompression: true))
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

        // A skybox panorama is a background image, not something specular response reads —
        // low sensitivity to block-compression quantization, same as Albedo/AO below.
        foreach (var s in def.Skyboxes)
            AddPath(texturePaths, s.Panorama);

        DecodeAssetsInParallelAndUpload([], texturePaths, []);
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
        var noCompressPaths = new HashSet<string>();

        foreach (var matPath in materialPaths)
        {
            var m = _materialRegistry.ReadDefinition(matPath);
            AddPath(texturePaths, AlbedoKey(m.Albedo, m.Opacity));
            AddPath(texturePaths, m.AO);
            AddPathNoCompress(texturePaths, noCompressPaths, m.Normal);
            AddPathNoCompress(texturePaths, noCompressPaths, m.Roughness);
            AddPathNoCompress(texturePaths, noCompressPaths, m.Metallic);
            AddPathNoCompress(texturePaths, noCompressPaths, m.Height);
        }

        DecodeAssetsInParallelAndUpload(modelPaths, texturePaths, noCompressPaths);
    }

    private void DecodeAssetsInParallelAndUpload(List<string> modelPaths, HashSet<string> texturePaths, HashSet<string> noCompressPaths)
    {
        // CPU decode off the GL thread
        var textureTask = Task.WhenAll(texturePaths.Select(key =>
            Task.Run(() => (key, data: DecodeTextureKey(key, allowCompression: !noCompressPaths.Contains(key))))));

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

    // Normal/Roughness/Metallic/Height all directly drive specular response (a normal map's XY
    // precision maps almost directly to lighting-direction error; roughness/metallic shape the
    // specular lobe itself) — BC1/BC3's RGB565 quantization is coarse enough to show up as visible
    // blocky artifacts exactly where it matters most, a highlight or reflection. This is standard,
    // well-known practice (real engines default these to BC5/uncompressed, never plain BC1/BC3),
    // not a Centauri-specific guess — see Docs/Documentation/TextureCompression.md's "Texture
    // roles" section for the regression this was cut from. Albedo/AO stay compression-eligible:
    // low-frequency color data tolerates the same quantization without a visible lighting effect.
    private static void AddPathNoCompress(HashSet<string> texturePaths, HashSet<string> noCompressPaths, string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        texturePaths.Add(path);
        noCompressPaths.Add(path);
    }

    private static string? AlbedoKey(string? albedo, string? opacity) =>
        albedo == null ? null : opacity == null ? albedo : $"{albedo}{OpacityKeyMarker}{opacity}";

    // Decode AND compress (GLTexture.CompressIfEligible — pure CPU, no GL calls) happen together
    // here rather than compression living in GLTexture's constructor, specifically so both run on
    // whatever thread calls this: a background Task.Run worker for the parallel-preload path
    // (DecodeAssetsInParallelAndUpload), same as decode already did, so the real per-texture CPU
    // cost of block-compressing a whole mip chain rides that existing parallelism instead of
    // landing serially on the GL thread afterward. For the on-demand single-texture path (a
    // material switched to a texture nothing preloaded), this still runs synchronously on
    // whatever thread asks for it — same as decode always did for that path; there's no batch to
    // parallelize against when it's only one texture.
    private TextureData DecodeTextureKey(string key, bool allowCompression)
    {
        var split = key.IndexOf(OpacityKeyMarker, StringComparison.Ordinal);
        var data = split < 0
            ? GLTexture.Decode(PathResolver.Resolve(key))
            : GLTexture.DecodeWithOpacity(
                PathResolver.Resolve(key[..split]),
                PathResolver.Resolve(key[(split + OpacityKeyMarker.Length)..]));

        GLTexture.CompressIfEligible(data, allowCompression && _config.Render.TextureCompression);
        return data;
    }

    // Textures.Get's own factory closure always allows compression (it's the Albedo/AO path — see
    // AddPathNoCompress's comment on why those two are safe to compress and the other PBR maps
    // aren't). For a role that needs compression suppressed, populate the cache with our own
    // instance first (if it's not already there) so the factory never runs, then fall through to
    // the ordinary Get for the resulting hit.
    private GLTexture GetTexture(string key, bool allowCompression)
    {
        if (!Textures.Contains(key))
            Textures.Insert(key, new GLTexture(_gl, DecodeTextureKey(key, allowCompression)));

        return Textures.Get(key);
    }

    private Material LoadMaterial(string path)
    {
        var def = _materialRegistry.ReadDefinition(path);

        var shader = Shaders.Get(def.Shader);

        return new Material(shader)
        {
            Name      = def.Name,
            Albedo    = AlbedoKey(def.Albedo, def.Opacity) is { } albedoKey ? Textures.Get(albedoKey) : null,
            Normal    = def.Normal    != null ? GetTexture(def.Normal,    allowCompression: false) : null,
            Roughness = def.Roughness != null ? GetTexture(def.Roughness, allowCompression: false) : null,
            Metallic  = def.Metallic  != null ? GetTexture(def.Metallic,  allowCompression: false) : null,
            AO        = def.AO        != null ? Textures.Get(def.AO)     : DefaultTexture,
            Height    = def.Height    != null ? GetTexture(def.Height,   allowCompression: false) : null,
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
