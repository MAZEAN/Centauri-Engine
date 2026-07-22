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

    private readonly List<Transform> _children = [];
    private Transform? _parent;

    public IReadOnlyList<Transform> Children => _children;
    public event Action? OnChanged;

    public Vector3 EulerAngles { get; private set; }

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
    
    public void Translate(Vector3 delta)       => Position += delta;
    public void RotateLocal(Quaternion delta)   => Rotation = delta * _rotation;
    public void RotateWorld(Quaternion delta)   => Rotation = _rotation * delta;
    
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, _rotation);
    public Vector3 WorldPosition => new(WorldMatrix.M41, WorldMatrix.M42, WorldMatrix.M43);

    public void SetEulerAngles(float pitchDeg, float yawDeg, float rollDeg)
    {
        EulerAngles = new Vector3(pitchDeg, yawDeg, rollDeg);
        Rotation = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(yawDeg),
            float.DegreesToRadians(pitchDeg),
            float.DegreesToRadians(rollDeg)
        );
    }

    // Sets an arbitrary orientation (e.g. from the rotate gizmo, which composes a world-axis
    // delta that isn't a single euler component) while keeping the EulerAngles cache the inspector
    // displays and edits from coherent — otherwise the inspector's Rotation rows would show a stale
    // value and its next drag would snap the object back to it. The extracted angles are one valid
    // (pitch, yaw, roll) that reproduces `rotation`; near a ±90° pitch gimbal there are many, and
    // roll is pinned to 0 there (see ToEulerDegrees).
    public void SetRotation(Quaternion rotation)
    {
        Rotation = rotation;                        // normalizes + marks dirty (existing setter)
        EulerAngles = ToEulerDegrees(_rotation);
    }

    // Inverse of CreateFromYawPitchRoll's Y(yaw)·X(pitch)·Z(roll) convention, via the rotation
    // matrix. System.Numerics matrices are row-vector (v' = v·M), so the column-vector element
    // Mc[r][c] used in the classic extraction below is m.M{c+1}{r+1} (a transpose).
    private static Vector3 ToEulerDegrees(Quaternion q)
    {
        var m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q));

        // Column-vector elements of R = Ry(yaw)·Rx(pitch)·Rz(roll):
        //   Mc[1][2] = -sin(pitch),  Mc[0][2] = sy·cp,  Mc[2][2] = cy·cp,
        //   Mc[1][0] = cp·sr,        Mc[1][1] = cp·cr.
        var mc12 = m.M32; // -sin(pitch)
        var mc02 = m.M31; //  sy·cp
        var mc22 = m.M33; //  cy·cp
        var mc10 = m.M12; //  cp·sr
        var mc11 = m.M22; //  cp·cr

        var pitch = MathF.Asin(Math.Clamp(-mc12, -1f, 1f));

        float yaw, roll;
        if (MathF.Abs(mc12) < 0.99999f)
        {
            yaw  = MathF.Atan2(mc02, mc22);
            roll = MathF.Atan2(mc10, mc11);
        }
        else
        {
            // Gimbal lock (pitch ≈ ±90°): yaw and roll rotate about the same screen axis, so their
            // split is arbitrary — pin roll to 0 and fold everything into yaw. Mc[0][0]=cy, Mc[2][0]=-sy
            // once sr=0/cr=1, i.e. m.M11 and m.M13.
            yaw  = MathF.Atan2(-m.M13, m.M11);
            roll = 0f;
        }

        return new Vector3(
            float.RadiansToDegrees(pitch),
            float.RadiansToDegrees(yaw),
            float.RadiansToDegrees(roll));
    }

    // checks if this transform is an ancestor of the given node
    private bool IsAncestorOf(Transform node)
    {
        var current = node._parent;
        while (current != null)
        {
            if (current == this) 
                return true;
            current = current._parent;
        }
        return false;
    }
}