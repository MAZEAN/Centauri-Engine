namespace Centauri.Loading;

using System.Text.Json;
using System.Numerics;

using Graphics.Resources;
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

    public SceneLoader(ResourceSystem resourceManager, Scene scene, AppConfig config)
    {
        _resourceSystem = resourceManager;
        _scene = scene;
        _path = config.Render.ScenePath;
        _config = config;
    }

    public void Load()
    {
        var fullPath = PathResolver.Resolve(_path);
        var json = File.ReadAllText(fullPath);
        var def  = JsonSerializer.Deserialize<SceneDefinition>(json, JsonDefaults.Options)
                   ?? throw new Exception($"Failed to deserialize scene file: {_path}");

        LoadEntities(def);
        LoadCameras(def);
        LoadSkyboxes(def);
    }

    private void LoadEntities(SceneDefinition def)
    {
        foreach (var e in def.Entities)
        {
            var model = !string.IsNullOrEmpty(e.Model)
                ? _resourceSystem.Models.Get(e.Model)
                : null;
            
            // Set default material if not a pure light source
            var material = !string.IsNullOrEmpty(e.Material)
                ? LoadMaterialFile(e.Material)
                : model is not null 
                    ? _resourceSystem.CreateDefaultMaterial() : null;
            
            Light? light = null;
            if (e.Light is { } l)
                light = CreateLight(l);

            var entity = new Entity(model, material, light);

            entity.Name = e.Name;

            entity.Transform.Position = new Vector3(e.Position[0], e.Position[1], e.Position[2]);
            entity.Transform.Scale    = new Vector3(e.Scale[0],    e.Scale[1],    e.Scale[2]);
            
            entity.UvScale  = new Vector2(e.UvScale[0],  e.UvScale[1]);
            entity.UvOffset = new Vector2(e.UvOffset[0], e.UvOffset[1]);
            
            entity.Enabled =  e.Enabled;

            if (e.Rotation is { Length: 3 })
                entity.Transform.SetEulerAngles(e.Rotation[0], e.Rotation[1], e.Rotation[2]);
            
            entity.Authored = new TransformSnapshot(
                entity.Transform.Position,
                entity.Transform.EulerAngles,
                entity.Transform.Scale
            );

            _scene.AddEntity(entity);
        }
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

    private Material LoadMaterialFile(string path)
    {
        var fullPath = PathResolver.Resolve(path);
        var json = File.ReadAllText(fullPath);
        var def  = JsonSerializer.Deserialize<MaterialDefinition>(json, JsonDefaults.Options)
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
            Color          = new Vector4(def.Color[0], def.Color[1], def.Color[2], def.Color[3])
        };
    }
    
    private void LoadSkyboxes(SceneDefinition def)
    {
        foreach (var s in def.Skyboxes)
            if (s.Cubemap.Length > 0)
                _scene.Skyboxes.Add(s.Name, _resourceSystem.CubeMaps.Get(s.Cubemap));

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