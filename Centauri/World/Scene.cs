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
    
    public int Revision { get; private set; }
    
    public LightingSystem Lighting { get; } = new();
    
    private readonly Dictionary<string, GLCubemap> _skyboxes = new();
    public IReadOnlyDictionary<string, GLCubemap> Skyboxes => _skyboxes;
    public GLCubemap? Skybox { get; private set; }
    
    private readonly List<Camera> _cameras = new();
    public IReadOnlyList<Camera> Cameras => _cameras;
    private Camera? _activeCamera, _primaryCamera;
    private readonly Dictionary<string, Camera> _cameraLookup = new();
    
    public Entity? Selected { get; private set; }

    public void Select(Entity? entity) => Selected = entity;
    public void ClearSelection() => Selected = null;

    public void AddEntity(Entity entity)
    {
        _entities.Add(entity);
        Revision++;
    }

    public void RemoveEntity(Entity entity)
    {
        _entities.Remove(entity);

        if (Selected == entity)        // keep selection valid
            Selected = null;

        Revision++;
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
        var best = float.MaxValue;

        foreach (var e in _entities)
        {
            if (!e.Enabled || e.Model is null) continue; // only renderable entities are pickable
            if (!e.GetWorldBounds().Intersects(ray, out var t) || !(t >= 0f) || !(t < best)) continue;
            
            best = t;
            hit  = e;
        }

        return hit;
    }
    
    public void AddSkybox(string name, GLCubemap cubemap)
    {
        _skyboxes[name] = cubemap;
        Skybox ??= cubemap;                          // first added becomes active
    }

    public void SetSkybox(string name)
    {
        if (!_skyboxes.TryGetValue(name, out var sky))
            throw new Exception($"Skybox '{name}' not found.");
        Skybox = sky;
    }
    
    public void Dispose()
    {
        Selected = null;

        foreach (var entity in _entities)
            entity.Dispose();

        _entities.Clear();
        _cameras.Clear();
        _cameraLookup.Clear();
    }
}