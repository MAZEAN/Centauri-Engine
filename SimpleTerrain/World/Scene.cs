namespace SimpleTerrain.World;

using Rendering.Resources;
using Config;
using Rendering.Systems;

public class Scene
{
    private readonly AppConfig _config;
    private readonly List<Entity> _entities = new();
    public IReadOnlyList<Entity> Entities => _entities;
    public LightingSystem Lighting { get; } = new();
    
    private readonly Dictionary<GLShader, List<Entity>> _shaderGroups = new();
    
    private bool _shaderGroupsDirty = true;

    public bool EnableCulling { get; private set; } = true;

    public Scene(AppConfig config)
    {
        _config = config;
    }

    public IReadOnlyDictionary<GLShader, List<Entity>> GetEntitiesByShader()
    {
        if (!_shaderGroupsDirty)
            return _shaderGroups;

        _shaderGroups.Clear();

        foreach (var entity in _entities)
        {
            var shader = entity.Material.Shader;

            if (!_shaderGroups.TryGetValue(shader, out var list))
            {
                list = new List<Entity>();
                _shaderGroups[shader] = list;
            }

            list.Add(entity);
        }

        // sort per shader group
        foreach (var list in _shaderGroups.Values)
        {
            list.Sort((a, b) =>
                a.Material.SortKey.CompareTo(b.Material.SortKey));
        }

        _shaderGroupsDirty = false;
        return _shaderGroups;
    }

    public void AddEntity(Entity entity)
    {
        _entities.Add(entity);
        _shaderGroupsDirty = true;
    }

    public void RemoveEntity(Entity entity)
    {
        _entities.Remove(entity);
        _shaderGroupsDirty = true;
    }
    
    private readonly List<Camera> _cameras = new();
    public IReadOnlyList<Camera> Cameras => _cameras;

    private readonly Dictionary<string, Camera> _cameraLookup = new();
    private Camera? _activeCamera;

    public void AddCamera(Camera cam)
    {
        if (_cameraLookup.ContainsKey(cam.Name))
            throw new Exception($"Camera with name '{cam.Name}' already exists.");

        _cameras.Add(cam);
        _cameraLookup[cam.Name] = cam;

        _activeCamera ??= cam;
    }

    public Camera GetActiveCamera()
    {
        return _activeCamera ?? throw new Exception("No active camera set.");
    }
    
    public Camera GetPrimaryCamera()
    {
        return Cameras.First(c => c.Name == _config.Camera.PrimaryCamera);
    }

    public void SetActiveCamera(string name)
    {
        if (!_cameraLookup.TryGetValue(name, out var cam))
            throw new Exception($"Camera '{name}' not found");

        _activeCamera = cam;
    }

    public void CycleCamera()
    {
        if (_cameras.Count == 0)
            throw new Exception("No cameras available.");

        if (_activeCamera == null)
        {
            _activeCamera = _cameras[0];
            return;
        }

        int index = _cameras.IndexOf(_activeCamera);
        index = (index + 1) % _cameras.Count;

        _activeCamera = _cameras[index];
    }

    public void ToggleEnableCulling()
    {
        EnableCulling = !EnableCulling;
    }
    
    public void Dispose()
    {
        foreach (var entity in _entities)
        {
            entity.Dispose();
        }

        _entities.Clear();
        _shaderGroups.Clear();
        _cameras.Clear();
        _cameraLookup.Clear();
    }
}