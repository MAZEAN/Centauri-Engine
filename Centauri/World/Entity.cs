namespace Centauri.World;

using System.Numerics;

using Graphics.Resources;
using Utils.Geometry;
using Graphics.Geometry;

public readonly record struct TransformSnapshot(Vector3 Position, Vector3 Euler, Vector3 Scale);

public class Entity : IDisposable
{
    public string    Name    { get; set; }
    public Model?    Model    { get; }   // optional now — a pure light has no mesh
    public Material? Material { get; }
    public Light?    Light    { get; set; }

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
    }
}