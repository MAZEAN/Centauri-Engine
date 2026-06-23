namespace Centauri.World;

using System.Numerics;

using Graphics.Resources.Materials;
using Utils.Geometry;
using Graphics.Geometry;
using Components;

public readonly record struct TransformSnapshot(Vector3 Position, Vector3 Euler, Vector3 Scale);

public class Entity : IDisposable
{
    public string    Name    { get; set; }
    public Model?    Model    { get; }
    public Material? Material { get; }
    public Light?    Light    { get; set; }
    
    private readonly List<Component> _components = new();
    public IReadOnlyList<Component> Components => _components;

    private BoundingBox _worldBounds;
    private bool _boundsDirty = true;
    private Transform _transform = new();

    public Vector2 UvScale  { get; set; } = Vector2.One;
    public Vector2 UvOffset { get; set; } = Vector2.Zero;

    public bool Enabled { get; set; } = true;
    public TransformSnapshot? Authored { get; set; }

    public Transform Transform
    {
        get => _transform;
        set
        {
            _transform.OnChanged -= OnTransformChanged;
            _transform            = value;
            _transform.OnChanged += OnTransformChanged;
            _boundsDirty          = true;
        }
    }

    public Entity(Model? model = null, Material? material = null, Light? light = null)
    {
        Model    = model;
        Material = material;
        Light    = light;
        _transform.OnChanged += OnTransformChanged;
    }
    
    public T AddComponent<T>(T component) where T : Component
    {
        _components.Add(component);
        component.Attach(this);
        return component;
    }
    
    public T? GetComponent<T>() where T : Component
    {
        foreach (var c in _components)
            if (c is T t) return t;
        return null;
    }
    
    public void Update(float dt)
    {
        for (var i = 0; i < _components.Count; i++)
            if (_components[i].Enabled)
                _components[i].Update(dt);
    }

    public BoundingBox GetWorldBounds()
    {
        if (Model is null)
            return default;

        if (_boundsDirty)
        {
            _worldBounds = Model.Bounds.Transform(Transform.WorldMatrix);
            _boundsDirty = false;
        }
        return _worldBounds;
    }

    private void OnTransformChanged() => _boundsDirty = true;

    public void Dispose()
    {
        _transform.OnChanged -= OnTransformChanged;
        
        foreach (var c in _components)
            if (c is IDisposable d) 
                d.Dispose();
    }
}