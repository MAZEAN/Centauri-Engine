namespace Centauri.Loading;

using System.Text.Json;
using System.Numerics;

using Config;
using Rendering;
using Utils.Misc;
using World;

// Loads the always-present half of a scene — cameras and skybox — from AppConfig's
// Render.EnvironmentPath. A project has exactly one environment; entity content is separate
// (see EntitySetLoader) and optional, so the same environment can be reused with a different
// preset entity set, or none at all, purely by editing config rather than the environment file.
public class EnvironmentLoader
{
    private readonly ResourceSystem _resourceSystem;
    private readonly Scene _scene;
    private readonly AppConfig _config;
    private readonly EntityFactory _factory;
    private readonly string _path;

    public EnvironmentLoader(ResourceSystem resourceSystem, Scene scene, AppConfig config)
    {
        _resourceSystem = resourceSystem;
        _scene = scene;
        _config = config;
        _factory = new EntityFactory(resourceSystem);
        _path = config.Render.EnvironmentPath;
    }

    public void Load()
    {
        var fullPath = PathResolver.Resolve(_path);
        var json = File.ReadAllText(fullPath);
        var def  = JsonSerializer.Deserialize<EnvironmentDefinition>(json, JsonDefaults.Options)
                   ?? throw new Exception($"Failed to deserialize environment file: {_path}");

        _resourceSystem.PreloadEnvironment(def);

        LoadCameras(def);
        LoadSkyboxes(def);
        LoadSun(def);
    }

    private void LoadSun(EnvironmentDefinition def)
    {
        if (def.Sun is { } sunDef)
            _scene.AddEntity(_factory.Build(sunDef));
    }

    private void LoadCameras(EnvironmentDefinition def)
    {
        if (def.Cameras.Count == 0)
            throw new Exception("Environment must contain at least one camera.");

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

        // active (view) camera — honor the environment's `active` flag, fall back to the first
        var active = def.Cameras.FirstOrDefault(c => c.Active) ?? def.Cameras[0];
        _scene.Cameras.SetActive(active.Name);

        // primary (culling) camera — honor `primary`, fall back to the active camera
        var primary = def.Cameras.FirstOrDefault(c => c.Primary) ?? active;
        _scene.Cameras.SetPrimary(primary.Name);
    }

    private void LoadSkyboxes(EnvironmentDefinition def)
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
