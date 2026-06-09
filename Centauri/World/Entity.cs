namespace Centauri.World;

using Rendering.Resources;
using Utils.Geometry;
using System.Numerics;

public class Entity : IDisposable
{
    public Model    Model    { get; }
    public Material Material { get; }
    
    private BoundingBox  _worldBounds;
    private bool _boundsDirty = true;
    private Transform _transform = new(); 
    
    public Vector2 UvScale  { get; set; } = Vector2.One;
    public Vector2 UvOffset { get; set; } = Vector2.Zero;
    
    public bool    Enabled   { get; set; } = true;

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

    public Entity(Model model, Material material)
    {
        Model    = model;
        Material = material;
        _transform.OnChanged += OnTransformChanged;
    }

    public BoundingBox GetWorldBounds()
    {
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
        Material.Dispose();
    }
}