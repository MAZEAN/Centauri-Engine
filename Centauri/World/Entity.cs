namespace Centauri.World;

using System.Numerics;

using Graphics.Resources.Materials;
using Utils.Geometry;
using Graphics.Geometry;
using Components;

public readonly record struct TransformSnapshot(Vector3 Position, Vector3 Euler, Vector3 Scale);

public class Entity : IDisposable
{
    public string  Name { get; set; } = null!;
    public bool    Enabled { get; set; } = true;
    public Model?  Model    { get; }
    public Light?  Light    { get; set; }

    // Materials
    private readonly Material?[] _materials;
    private readonly bool[]      _ownsMaterial;   // per-submesh copy-on-write tracking
    public IReadOnlyList<Material?> Materials => _materials;
    
    // Components
    private readonly List<Component> _components = new();
    public IReadOnlyList<Component> Components => _components;
    
    // Geometry
    private BoundingBox _worldBounds;
    private bool _boundsDirty = true;
    private bool? _anyTwoSided;
    private Transform _transform = new();
    
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
    
    public TransformSnapshot? Authored { get; set; }

    public Entity(Model? model = null, Material?[]? materials = null, Light? light = null)
    {
        Model         = model;
        _materials    = materials ?? Array.Empty<Material?>();
        _ownsMaterial = new bool[_materials.Length];
        Light         = light;
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
            if (c is T t)
                return t;
        return null;
    }

    // Detaches the first component of type T, if any. Callers that need cleanup beyond removal
    // from this list (e.g. PhysicsSystem releasing a RigidBody's BEPU handle) are responsible for
    // noticing the detach themselves — this is deliberately just list bookkeeping, matching
    // AddComponent/GetComponent's own scope.
    public bool RemoveComponent<T>() where T : Component
    {
        for (var i = 0; i < _components.Count; i++)
        {
            if (_components[i] is not T) continue;
            _components.RemoveAt(i);
            return true;
        }
        return false;
    }
    
    public void Update(float dt)
    {
        for (var i = 0; i < _components.Count; i++)
            if (_components[i].Enabled)
                _components[i].Update(dt);
    }
    
    public bool AnyTwoSided
    {
        get
        {
            if (_anyTwoSided is { } cached) 
                return cached;

            var any = false;
            foreach (var m in _materials)
                if (m is { TwoSided: true })
                {
                    any = true; 
                    break;
                }

            _anyTwoSided = any;
            return any;
        }
    }
    
    public bool MakeMaterialUnique(int index)
    {
        if ((uint)index >= (uint)_materials.Length || _ownsMaterial[index] || _materials[index] is null)
            return false;

        _materials[index]    = _materials[index]!.Clone();
        _ownsMaterial[index] = true;
        _anyTwoSided         = null;

        return true;
    }

    // Swaps a mesh slot to a different material asset entirely — distinct from
    // MakeMaterialUnique, which clones the *current* material so its scalar properties can be
    // tweaked without affecting other entities sharing it. This replaces the reference outright,
    // so the slot goes back to sharing the cache-owned Material (not a per-entity clone) until/
    // unless it's edited again.
    public void SetMaterial(int index, Material material)
    {
        if ((uint)index >= (uint)_materials.Length) return;

        _materials[index]    = material;
        _ownsMaterial[index] = false;
        _anyTwoSided         = null;
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