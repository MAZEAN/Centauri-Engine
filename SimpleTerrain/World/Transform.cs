namespace SimpleTerrain.World;

using System.Numerics;

public class Transform
{
    private Vector3 _position = Vector3.Zero;
    private Vector3 _scale = Vector3.One;
    private Quaternion _rotation = Quaternion.Identity;

    private Matrix4x4 _localMatrix;
    private Matrix4x4 _worldMatrix;

    private bool _localDirty = true;
    private bool _worldDirty = true;

    private readonly List<Transform> _children = new();
    public IReadOnlyList<Transform> Children => _children;
    private Transform? _parent;
    
    public Transform? Parent
    {
        get => _parent;
        set
        {
            if (value == this)
                throw new Exception("Transform cannot be parent of itself");
            
            if (value != null && IsDescendantOf(value))
                throw new Exception("Cannot assign parent: cycle detected.");


            if (_parent == value) return;

            // remove from old parent
            _parent?._children.Remove(this);

            _parent = value;

            // add to new parent
            _parent?._children.Add(this);

            MarkWorldDirty();
        }
    }


    // -----------------------------
    // Properties
    // -----------------------------
    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            MarkDirty();
        }
    }

    public Vector3 Scale
    {
        get => _scale;
        set
        {
            _scale = value;
            MarkDirty();
        }
    }

    public Quaternion Rotation
    {
        get => _rotation;
        set
        {
            _rotation = Quaternion.Normalize(value);
            MarkDirty();
        }
    }

    // -----------------------------
    // Dirty propagation
    // -----------------------------
    private void MarkDirty()
    {
        _localDirty = true;
        MarkWorldDirty();
    }

    private void MarkWorldDirty()
    {
        _worldDirty = true;

        foreach (var child in _children)
            child.MarkWorldDirty();
    }

    // -----------------------------
    // Matrices
    // -----------------------------
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
            if (_worldDirty)
            {
                
                var local = LocalMatrix;

                _worldMatrix = Parent != null
                    ? local * Parent.WorldMatrix
                    : local;

                _worldDirty = false;
            }

            return _worldMatrix;
        }
    }

    // -----------------------------
    // Transform helpers
    // -----------------------------
    public void Translate(Vector3 delta)
    {
        Position += delta;
    }

    public void RotateLocal(Quaternion delta)
    {
        Rotation = delta * _rotation;
    }

    public void RotateWorld(Quaternion delta)
    {
        Rotation = _rotation * delta;
    }

    public void SetEulerAngles(float pitchDeg, float yawDeg, float rollDeg)
    {
        Rotation = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(yawDeg),
            float.DegreesToRadians(pitchDeg),
            float.DegreesToRadians(rollDeg)
        );
    }
    
    private bool IsDescendantOf(Transform potentialParent)
    {
        var current = potentialParent;

        while (current != null)
        {
            if (current == this)
                return true;

            current = current.Parent;
        }
        return false;
    }
}