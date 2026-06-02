namespace SimpleTerrain.Scene;

using System.Numerics;
using Config;
using Silk.NET.Maths;

using Utils;

public class Camera
{
    public string Name { get; }

    private readonly CameraConfig _config;

    private Vector3 _position;
    public Vector3 Position => _position;

    private Vector3 _forward;
    public Vector3 Forward => _forward;

    private Vector3 _right;
    public Vector3 Right => _right;

    private Vector3 _up;
    public Vector3 Up => _up;

    private readonly Vector3 _worldUp;

    private float _yaw;
    private float _pitch;

    private float _zoom;
    private float _aspectRatio;

    public Camera(CameraConfig config, string name, Vector3 position, Vector3 worldUp, float yaw, float pitch)
    {
        _config = config;
        Name = name;

        _position = position;
        _worldUp = worldUp;

        _yaw = yaw;
        _pitch = pitch;
        _zoom = config.FOV;

        UpdateVectors();
    }
    
    public void UpdatePosition(Vector3 delta)
    {
        _position += delta;
    }
    
    public void ModifyDirection(float xOffset, float yOffset)
    {
        _yaw   += xOffset;
        _pitch += -yOffset;

        // clamp pitch
        _pitch = Math.Clamp(_pitch, -89f, 89f);

        UpdateVectors();
    }

    public void AdjustZoom(float zoomDelta)
    {
        _zoom = Math.Clamp(_zoom + zoomDelta, _config.MinZoom, _config.MaxZoom);
    }
    
    private void UpdateVectors()
    {
        var cameraDirection = Vector3.Zero;
        cameraDirection.X = MathF.Cos(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch));
        cameraDirection.Y = MathF.Sin(MathHelper.DegreesToRadians(_pitch));
        cameraDirection.Z = MathF.Sin(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch));

        _forward = Vector3.Normalize(cameraDirection);

        // ✅ derive right + up
        _right = Vector3.Normalize(Vector3.Cross(_forward, _worldUp));
        _up    = Vector3.Normalize(Vector3.Cross(_right, _forward));
    }
    
    public void SetAspectRatio(Vector2D<int> newSize)
    {
        _aspectRatio = (float) newSize.X / newSize.Y;
    }
    
    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(_position, _position + _forward, _up);
    }
    
    public Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(_zoom), _aspectRatio, _config.Near, _config.Far);
    }
    
    public Vector3 GetPosition()
    {
        return _position;
    }
}