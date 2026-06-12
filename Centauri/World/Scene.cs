namespace Centauri.World;

using System.Diagnostics;
using Silk.NET.Windowing;

using Rendering.Resources;
using Rendering.Systems;
using Utils.Geometry;

public class Scene
{
    private readonly List<Entity> _entities = new();
    public IReadOnlyList<Entity> Entities => _entities;
    private readonly Dictionary<GLShader, List<Entity>> _shaderGroups = new();
    
    private bool _shaderGroupsDirty = true;
    
    public LightingSystem Lighting { get; } = new();
    
    private readonly List<Camera> _cameras = new();
    public IReadOnlyList<Camera> Cameras => _cameras;
    private Camera? _activeCamera, _primaryCamera;
    private readonly Dictionary<string, Camera> _cameraLookup = new();
    
    public Entity? Selected { get; private set; }

    public void Select(Entity? entity) => Selected = entity;
    public void ClearSelection() => Selected = null;

    public IReadOnlyDictionary<GLShader, List<Entity>> GetEntitiesByShader()
    {
        if (!_shaderGroupsDirty)
            return _shaderGroups;

        _shaderGroups.Clear();

        foreach (var entity in _entities)
        {
            if (entity.Material is not { } material)   // light-only / mesh-less entities
                continue;

            var shader = material.Shader;

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
            {
                Debug.Assert(a.Material != null);
                Debug.Assert(b.Material != null);
                return a.Material.SortKey.CompareTo(b.Material.SortKey);
            });
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

        if (Selected == entity)        // keep selection valid
            Selected = null;

        _shaderGroupsDirty = true;
    }

    public void InitializeCameras(IWindow window)
    {
        foreach (var cam in Cameras)
            cam.SetAspectRatio(window.FramebufferSize);
    }

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
        => _primaryCamera ?? _activeCamera
            ?? throw new Exception("No primary camera set.");

    public void SetPrimaryCamera(string name)
    {
        if (!_cameraLookup.TryGetValue(name, out var cam))
            throw new Exception($"Primary camera '{name}' not found");

        _primaryCamera = cam;
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

        var index = _cameras.IndexOf(_activeCamera);
        index = (index + 1) % _cameras.Count;

        _activeCamera = _cameras[index];
    }

    public Entity? Pick(Ray ray)
    {
        Entity? hit = null;
        float best = float.MaxValue;

        foreach (var e in _entities)
        {
            if (!e.Enabled || e.Model is null) continue; // only renderable entities are pickable

            if (e.GetWorldBounds().Intersects(ray, out var t) && t >= 0f && t < best)
            {
                best = t;
                hit  = e;
            }
        }

        return hit;
    }
    
    public void Dispose()
    {
        Selected = null;

        foreach (var entity in _entities)
            entity.Dispose();

        _entities.Clear();
        _shaderGroups.Clear();
        _cameras.Clear();
        _cameraLookup.Clear();
    }
}