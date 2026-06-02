namespace SimpleTerrain.Scene;

using System.Text.Json;
using System.Numerics;
using Silk.NET.OpenGL;

using Rendering;
using Lighting;
using Config;
using Utils;

public class SceneLoader : IDisposable
{
    private readonly GL _gl;
    private readonly Scene _scene;
    private readonly string _path;
    private readonly AppConfig _config;
    
    private AssetCache<GLTexture> _textures;
    private AssetCache<GLShader> _shaders;
    private AssetCache<Model> _models;
    
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SceneLoader(GL gl, Scene scene, AppConfig config)
    {
        _gl = gl;
        _scene = scene;
        _path = config.Render.ScenePath;
        _config = config;
        
        InitializeCaches();
    }

    public void Load()
    {
        var json = File.ReadAllText(_path);
        var def  = JsonSerializer.Deserialize<SceneDefinition>(json, Options)
                   ?? throw new Exception($"Failed to deserialize scene file: {_path}");
        
        LoadEntities(def);
        LoadLighting(def);
        LoadCameras(def);
    }

    private void LoadEntities(SceneDefinition def)
    {
        foreach (var e in def.Entities)
        {
            var model = _models.Get(e.Model);
            var material = e.Material != null
                ? LoadMaterialFile(e.Material)
                : throw new Exception($"Entity '{e.Name}' must have a material file.");

            var entity = new Entity(model, material);

            entity.Transform.Position = new Vector3(e.Position[0], e.Position[1], e.Position[2]);
            entity.Transform.Scale    = new Vector3(e.Scale[0],    e.Scale[1],    e.Scale[2]);

            if (e.Rotation is { Length: 3 })
                entity.Transform.SetEulerAngles(e.Rotation[0], e.Rotation[1], e.Rotation[2]);

            _scene.AddEntity(entity);
        }
    }

    private void LoadLighting(SceneDefinition def)
    {
        foreach (var d in def.Lights.Directional)
        {
            _scene.Lighting.Add(new DirectionalLight
            {
                Direction = new Vector3(d.Direction[0], d.Direction[1], d.Direction[2]),
                Color     = new Vector3(d.Color[0],     d.Color[1],     d.Color[2]),
                Intensity = d.Intensity,
                Enabled   = d.Enabled
            });
        }

        foreach (var p in def.Lights.Point)
        {
            _scene.Lighting.Add(new PointLight
            {
                Position  = new Vector3(p.Position[0], p.Position[1], p.Position[2]),
                Color     = new Vector3(p.Color[0],    p.Color[1],    p.Color[2]),
                Intensity = p.Intensity,
                Constant  = p.Constant,
                Linear    = p.Linear,
                Quadratic = p.Quadratic,
                Enabled   = p.Enabled
            });
        }

        foreach (var s in def.Lights.Spot)
        {
            _scene.Lighting.Add(new SpotLight
            {
                Position    = new Vector3(s.Position[0],  s.Position[1],  s.Position[2]),
                Direction   = new Vector3(s.Direction[0], s.Direction[1], s.Direction[2]),
                Color       = new Vector3(s.Color[0],     s.Color[1],     s.Color[2]),
                Intensity   = s.Intensity,
                InnerCutoff = s.InnerCutoff,
                OuterCutoff = s.OuterCutoff,
                Enabled     = s.Enabled
            });
        }
    }

    private void LoadCameras(SceneDefinition def)
    {
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

            _scene.AddCamera(camera);
        }
        
        if (def.Cameras.Count == 0)
        {
            throw new Exception("Scene must contain at least one camera.");
        }
        
        var primaryName = _config.Camera.PrimaryCamera;
        var primary = def.Cameras.FirstOrDefault(c => c.Name == primaryName);
        
        if (primary != null)
        {
            _scene.SetActiveCamera(primaryName);
        }
        else
        {
            _scene.SetActiveCamera(def.Cameras[0].Name);
        }
    }

    private Material LoadMaterialFile(string path)
    {
        var json = File.ReadAllText(path);
        var def  = JsonSerializer.Deserialize<MaterialDefinition>(json, Options)
                   ?? throw new Exception($"Failed to deserialize material file: {path}");

        var shader = _shaders.Get(def.Shader);

        return new Material(shader)
        {
            Albedo    = def.Albedo    != null ? _textures!.Get(def.Albedo)    : null,
            Normal    = def.Normal    != null ? _textures!.Get(def.Normal)    : null,
            Roughness = def.Roughness != null ? _textures!.Get(def.Roughness) : null,
            Metallic  = def.Metallic  != null ? _textures!.Get(def.Metallic)  : null,
            AO        = def.AO        != null ? _textures!.Get(def.AO)        : null,
            RoughnessValue = def.RoughnessValue,
            MetallicValue  = def.MetallicValue,
            Color          = new Vector4(def.Color[0],   def.Color[1],   def.Color[2],   def.Color[3]),
            UvScale        = new Vector2(def.UvScale[0],  def.UvScale[1]),
            UvOffset       = new Vector2(def.UvOffset[0], def.UvOffset[1])
        };
    }

    private void InitializeCaches()
    {
        _textures = new AssetCache<GLTexture>(
            _config.Render.TextureCacheSize,
            path => new GLTexture(_gl, Path.GetFullPath(path))
        );

        _shaders = new AssetCache<GLShader>(
            _config.Render.ShaderCacheSize,
            shaderBase =>
                new GLShader(
                    _gl,
                    shaderBase + ".vert",
                    shaderBase + ".frag"));

        _models = new AssetCache<Model>(
            _config.Render.ModelCacheSize,
            path => new Model(_gl, path));
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

    public void Dispose()
    {
        _textures.Dispose();
        _shaders.Dispose();
        _models.Dispose();
    }
}