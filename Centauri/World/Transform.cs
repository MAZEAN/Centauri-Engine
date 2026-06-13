namespace Centauri.World;
using System.Numerics;

public class Transform
{
    private Vector3    _position = Vector3.Zero;
    private Vector3    _scale    = Vector3.One;
    private Quaternion _rotation = Quaternion.Identity;

    private Matrix4x4 _localMatrix;
    private Matrix4x4 _worldMatrix;
    private bool      _localDirty = true;
    private bool      _worldDirty = true;

    private readonly List<Transform> _children = new();
    private Transform? _parent;

    public IReadOnlyList<Transform> Children => _children;
    public event Action? OnChanged;
    
    private Vector3 _eulerAngles; // last-set degrees (pitch, yaw, roll) — kept in sync by SetEulerAngles

    public Vector3 EulerAngles => _eulerAngles;

    public Transform? Parent
    {
        get => _parent;
        set
        {
            if (value == this)
                throw new InvalidOperationException("Transform cannot be its own parent.");

            if (value != null && IsAncestorOf(value))
                throw new InvalidOperationException("Cannot assign parent: would create a cycle.");

            if (_parent == value) return;

            _parent?._children.Remove(this);
            _parent = value;
            _parent?._children.Add(this);

            MarkWorldDirty();
        }
    }

    // ── Properties ────────────────────────────────────────────────────────────

    public Vector3 Position
    {
        get => _position;
        set { _position = value; MarkDirty(); }
    }

    public Vector3 Scale
    {
        get => _scale;
        set { _scale = value; MarkDirty(); }
    }

    public Quaternion Rotation
    {
        get => _rotation;
        set { _rotation = Quaternion.Normalize(value); MarkDirty(); }
    }

    // ── Dirty propagation ─────────────────────────────────────────────────────

    private void MarkDirty()
    {
        _localDirty = true;
        MarkWorldDirty();
    }

    private void MarkWorldDirty()
    {
        if (_worldDirty) return; // already dirty — stop propagation

        _worldDirty = true;
        OnChanged?.Invoke();

        foreach (var child in _children)
            child.MarkWorldDirty();
    }

    // ── Matrices ──────────────────────────────────────────────────────────────

    public Matrix4x4 LocalMatrix
    {
        get
        {
            if (_localDirty)
            {
                _localMatrix =
                    Matrix4x4.CreateScale(_scale) *
                    Matrix4x4.CreateFromQuaternion(_rotation) *
                    Matrix4x4.CreateTranslation(_position);
                _localDirty = false;
            }
            return _localMatrix;
        }
    }

    public Matrix4x4 WorldMatrix
    {
        get
        {
            if (!_worldDirty) 
                return _worldMatrix;
            
            _worldMatrix = Parent != null
                ? LocalMatrix * Parent.WorldMatrix
                : LocalMatrix;
            _worldDirty = false;
            
            return _worldMatrix;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public void Translate(Vector3 delta)       => Position += delta;
    public void RotateLocal(Quaternion delta)   => Rotation = delta * _rotation;
    public void RotateWorld(Quaternion delta)   => Rotation = _rotation * delta;
    
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, _rotation);
    public Vector3 WorldPosition => new(WorldMatrix.M41, WorldMatrix.M42, WorldMatrix.M43);

    public void SetEulerAngles(float pitchDeg, float yawDeg, float rollDeg)
    {
        _eulerAngles = new Vector3(pitchDeg, yawDeg, rollDeg);
        Rotation = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(yawDeg),
            float.DegreesToRadians(pitchDeg),
            float.DegreesToRadians(rollDeg)
        );
    }

    // checks if this transform is an ancestor of the given node
    private bool IsAncestorOf(Transform node)
    {
        var current = node._parent;
        while (current != null)
        {
            if (current == this) return true;
            current = current._parent;
        }
        return false;
    }
}