namespace Centauri.Loading;

using System.Text.Json;
using System.Numerics;

using Config;
using Rendering;
using Utils.Misc;
using World;
using World.Collections;

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

    // The sun isn't tracked the way EntitySetLoader tracks entity-set entities (TrackedEntitySet)
    // — there's only ever at most one, and it's not subject to CreateEntity/DeleteEntity — but
    // Save still needs both the live Entity (for whatever's actually changed — Transform, Light,
    // Enabled) and the EntityDefinition it came from (for the fields the inspector doesn't
    // round-trip live: Components — same split EntityDefinitionWriter.Write already makes for
    // ordinary entities).
    private Entity? _sunEntity;
    private EntityDefinition? _sunSource;

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

    // Writes cameras, skyboxes, and the sun back out — the counterpart to EntitySetLoader.Save()
    // for the half of a scene that loader doesn't own. Camera Position/Yaw/Pitch reflect however
    // far the camera's actually been flown/dragged since load (Camera has no separate "authored"
    // vs. "live" split the way entity Transforms do); Active/Primary reflect whichever camera is
    // current (switched via the 'C' cycle hotkey). Skybox Exposure/BlackLevel reflect the
    // inspector's live sliders (SkyboxSection); Name/Panorama/Active aren't live-editable (see
    // Skybox's own comment) but are carried through unchanged rather than lost. Doesn't attempt a
    // symmetric Reset() the way EntitySetLoader has (Ctrl+Shift+R) — reloading cameras/skyboxes
    // live would mean rebuilding GPU-cached textures and IBL bakes mid-session, real added scope
    // this pass doesn't cover; see Docs/Documentation/EnvironmentPersistence.md.
    public void Save()
    {
        var def = new EnvironmentDefinition
        {
            Cameras  = _scene.Cameras.All.Select(ToDefinition).ToList(),
            Skyboxes = _scene.Skyboxes.All.Select(ToDefinition).ToList(),
            Sun      = _sunEntity is not null && _sunSource is not null
                ? EntityDefinitionWriter.Write(_sunEntity, _sunSource, parentName: null)
                : _sunSource,
        };

        var json = JsonSerializer.Serialize(def, JsonDefaults.Options);
        File.WriteAllText(PathResolver.Resolve(_path), json);
    }

    private void LoadSun(EnvironmentDefinition def)
    {
        if (def.Sun is not { } sunDef) return;

        _sunSource = sunDef;
        _sunEntity = _factory.Build(sunDef);
        _scene.AddEntity(_sunEntity);
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
                _scene.Skyboxes.Add(s.Name, s.Panorama, _resourceSystem.Textures.Get(s.Panorama), s.Exposure, s.BlackLevel);

        // honor an `active` flag like cameras do; otherwise the first stays active
        var active = def.Skyboxes.FirstOrDefault(s => s.Active);
        if (active is { Name.Length: > 0 })
            _scene.Skyboxes.SetActive(active.Name);
    }

    private CameraDefinition ToDefinition(Camera cam) => new()
    {
        Name     = cam.Name,
        Position = [cam.Position.X, cam.Position.Y, cam.Position.Z],
        Up       = FormatUp(cam.WorldUp),
        Yaw      = cam.Yaw,
        Pitch    = cam.Pitch,
        Active   = ReferenceEquals(cam, _scene.Cameras.Active),
        Primary  = ReferenceEquals(cam, _scene.Cameras.Primary),
    };

    private SkyboxDefinition ToDefinition(Skybox sky) => new()
    {
        Name       = sky.Name,
        Panorama   = sky.PanoramaPath,
        Exposure   = sky.Exposure,
        BlackLevel = sky.BlackLevel,
        Active     = ReferenceEquals(sky, _scene.Skyboxes.Active),
    };

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

    // Inverse of ParseUp — WorldUp is only ever set once, at construction, from one of the three
    // unit axes above (never live-edited), so an exact-equality check is enough; Y is the
    // fallback both because it's the overwhelmingly common case and because it keeps this total
    // rather than throwing on a WorldUp that (in principle) could be something ParseUp itself
    // never actually produces.
    private static string FormatUp(Vector3 up) =>
        up == Vector3.UnitX ? "X" : up == Vector3.UnitZ ? "Z" : "Y";
}
