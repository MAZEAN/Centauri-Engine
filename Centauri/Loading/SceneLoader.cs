namespace Centauri.Loading;

using System.Text.Json;
using System.Numerics;

using Rendering.Resources;
using Config;
using Rendering.Systems;
using Utils.Misc;
using World;

public class SceneLoader
{
    private readonly ResourceSystem _resourceSystem;
    private readonly Scene _scene;
    private readonly AppConfig _config;
    
    private readonly string _path;
    
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SceneLoader(ResourceSystem resourceManager, Scene scene, AppConfig config)
    {
        _resourceSystem = resourceManager;
        _scene = scene;
        _path = config.Render.ScenePath;
        _config = config;
    }

    public void Load()
    {
        var fullPath = AssetPath.Resolve(_path);
        var json = File.ReadAllText(fullPath);
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
            var model = _resourceSystem.Models.Get(e.Model);
            var material = e.Material != null
                ? LoadMaterialFile(e.Material)
                : throw new Exception($"Entity '{e.Name}' must have a material file.");

            var entity = new Entity(model, material);

            entity.Transform.Position = new Vector3(e.Position[0], e.Position[1], e.Position[2]);
            entity.Transform.Scale    = new Vector3(e.Scale[0],    e.Scale[1],    e.Scale[2]);
            
            entity.UvScale  = new Vector2(e.UvScale[0],  e.UvScale[1]);
            entity.UvOffset = new Vector2(e.UvOffset[0], e.UvOffset[1]);
            
            entity.Enabled =  e.Enabled;

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
        var fullPath = AssetPath.Resolve(path);
        var json = File.ReadAllText(fullPath);
        var def  = JsonSerializer.Deserialize<MaterialDefinition>(json, Options)
                   ?? throw new Exception($"Failed to deserialize material file: {path}");

        var shader = _resourceSystem.Shaders.Get(def.Shader);

        return new Material(shader)
        {
            Albedo    = def.Albedo    != null ? _resourceSystem.Textures.Get(def.Albedo)    : null,
            Normal    = def.Normal    != null ? _resourceSystem.Textures.Get(def.Normal)    : null,
            Roughness = def.Roughness != null ? _resourceSystem.Textures.Get(def.Roughness) : null,
            Metallic  = def.Metallic  != null ? _resourceSystem.Textures.Get(def.Metallic)  : null,
            AO        = def.AO        != null ? _resourceSystem.Textures.Get(def.AO)         : _resourceSystem.DefaultTexture, 
            RoughnessValue = def.RoughnessValue,
            MetallicValue  = def.MetallicValue,
            Color          = new Vector4(def.Color[0],   def.Color[1],   def.Color[2],   def.Color[3])
        };
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