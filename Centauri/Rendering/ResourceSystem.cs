namespace Centauri.Rendering;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Text.Json;

using Utils.Caching;
using Graphics.Resources;
using Graphics.Resources.Materials;
using Config;
using Loading;
using Utils.Misc;
using Graphics.Geometry;

public class ResourceSystem : IDisposable
{
    private readonly GL _gl;
    private readonly AppConfig _config;
    
    public AssetCache<GLTexture> Textures { get; }
    public AssetCache<GLShader> Shaders { get; }
    public AssetCache<Model> Models { get; }
    
    private readonly Dictionary<string, Material> _materials = new();
    private Material? _defaultMaterial;
    public GLTexture DefaultTexture { get; private set; }

    public ResourceSystem(GL gl, AppConfig config)
    {
        _gl = gl;
        _config = config;
        
        Textures = new AssetCache<GLTexture>(
            path => new GLTexture(gl, PathResolver.Resolve(path))
        );

        Shaders = new AssetCache<GLShader>(
            shaderBase => new GLShader(gl,
                PathResolver.Resolve(shaderBase + ".vert"),
                PathResolver.Resolve(shaderBase + ".frag")));

        Models = new AssetCache<Model>(
            path => new Model(gl, PathResolver.Resolve(path)));

        DefaultTexture = CreateDefaultTexture(gl);
    }
    
    private static GLTexture CreateDefaultTexture(GL gl)
    {
        Span<byte> pixel = [255, 255, 255, 255];
        return new GLTexture(gl, pixel, 1, 1);
    }
    
    public Material DefaultMaterial
        => _defaultMaterial ??= new(Shaders.Get(_config.Render.DefaultShader)) { AO = DefaultTexture };
    
    public Material GetMaterial(string path)
    {
        if (_materials.TryGetValue(path, out var material))
            return material;

        material = LoadMaterial(path);
        _materials[path] = material;
        return material;
    }
    
    public void PreloadScene(SceneDefinition def)
    {
        var modelPaths = def.Entities
            .Where(e => !string.IsNullOrEmpty(e.Model))
            .Select(e => e.Model!)
            .Distinct()
            .ToList();

        var texturePaths = new HashSet<string>();
        foreach (var e in def.Entities)
        {
            if (string.IsNullOrEmpty(e.Material)) continue;
            var m = ReadMaterialDef(e.Material);
            AddPath(texturePaths, m.Albedo);
            AddPath(texturePaths, m.Normal);
            AddPath(texturePaths, m.Roughness);
            AddPath(texturePaths, m.Metallic);
            AddPath(texturePaths, m.AO);
        }
        foreach (var s in def.Skyboxes)
            AddPath(texturePaths, s.Panorama);

        // CPU decode off the GL thread
        var textureJob = Task.WhenAll(texturePaths.Select(key =>
            Task.Run(() => (key, data: GLTexture.Decode(PathResolver.Resolve(key))))));

        var modelJob = Task.Run(() => modelPaths
            .Select(key => (key, data: Model.Decode(PathResolver.Resolve(key))))
            .ToList());

        Task.WaitAll(textureJob, modelJob);

        // GL creation on the calling (main) thread
        foreach (var (key, data) in textureJob.Result)
            Textures.Insert(key, new GLTexture(_gl, data));

        foreach (var (key, data) in modelJob.Result)
            Models.Insert(key, new Model(_gl, data));
    }

    private static void AddPath(HashSet<string> set, string? path)
    {
        if (!string.IsNullOrEmpty(path)) set.Add(path);
    }

    private static MaterialDefinition ReadMaterialDef(string path)
    {
        var json = File.ReadAllText(PathResolver.Resolve(path));
        return JsonSerializer.Deserialize<MaterialDefinition>(json, JsonDefaults.Options)
               ?? throw new Exception($"Failed to deserialize material file: {path}");
    }

    private Material LoadMaterial(string path)
    {
        var def = ReadMaterialDef(path);

        var shader = Shaders.Get(def.Shader);

        return new Material(shader)
        {
            Albedo    = def.Albedo    != null ? Textures.Get(def.Albedo)    : null,
            Normal    = def.Normal    != null ? Textures.Get(def.Normal)    : null,
            Roughness = def.Roughness != null ? Textures.Get(def.Roughness) : null,
            Metallic  = def.Metallic  != null ? Textures.Get(def.Metallic)  : null,
            AO        = def.AO        != null ? Textures.Get(def.AO)         : DefaultTexture,
            RoughnessValue = def.RoughnessValue,
            MetallicValue  = def.MetallicValue,
            Color          = new Vector4(def.Color[0], def.Color[1], def.Color[2], def.Color[3]),
            TwoSided       = def.TwoSided
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