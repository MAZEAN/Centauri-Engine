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
    
    private readonly AppConfig _config;
    public AssetCache<GLTexture> Textures { get; }
    public AssetCache<GLShader> Shaders { get; }
    public AssetCache<Model> Models { get; }
    
    private readonly Dictionary<string, Material> _materials = new();
    private Material? _defaultMaterial;
    public GLTexture DefaultTexture { get; private set; }

    public ResourceSystem(GL gl, AppConfig config)
    {
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

    private Material LoadMaterial(string path)
    {
        var fullPath = PathResolver.Resolve(path);
        var json = File.ReadAllText(fullPath);
        var def  = JsonSerializer.Deserialize<MaterialDefinition>(json, JsonDefaults.Options)
                   ?? throw new Exception($"Failed to deserialize material file: {path}");

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