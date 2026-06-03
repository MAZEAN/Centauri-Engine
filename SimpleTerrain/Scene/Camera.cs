using Silk.NET.Assimp;

namespace SimpleTerrain.Scene;

using System.Numerics;
using Silk.NET.Maths;
using Plane = System.Numerics.Plane;

using Config;
using Utils;

public class Camera
{
    public string Name { get; }

    private readonly CameraConfig _config;
    public Vector3 Position { get; private set; }
    public Vector3 Forward { get; private set;}
    public Vector3 Right { get; private set;}
    public Vector3 Up { get; private set;}

    private readonly Vector3 _worldUp;

    private float _yaw;
    private float _pitch;
    public float Zoom { get; private set;}
    public float AspectRatio { get; private set;}
    
    public Frustum Frustum { get; private set;}

    public Camera(CameraConfig config, string name, Vector3 position, Vector3 worldUp, float yaw, float pitch)
    {
        _config = config;
        Name = name;

        Position = position;
        _worldUp = worldUp;

        _yaw = yaw;
        _pitch = pitch;
        Zoom = config.FOV;

        Frustum = new Frustum(this, _config);

        UpdateVectors();
    }
    
    public void UpdatePosition(Vector3 delta)
    {
        Position += delta;
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
        Zoom = Math.Clamp(Zoom + zoomDelta, _config.MinZoom, _config.MaxZoom);
    }
    
    private void UpdateVectors()
    {
        float yawRad = MathHelper.DegreesToRadians(_yaw);
        float pitchRad = MathHelper.DegreesToRadians(_pitch);

        var direction = new Vector3(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad)
        );

        Forward = Vector3.Normalize(direction);
        
        Right = Vector3.Normalize(Vector3.Cross(Forward, _worldUp));
        Up    = Vector3.Normalize(Vector3.Cross(Right, Forward));
    }
    
    public void SetAspectRatio(Vector2D<int> newSize)
    {
        if (newSize.Y <= 0)
            throw new ArgumentException("Height must be positive.");
        AspectRatio = (float)newSize.X / newSize.Y;
    }
    
    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Position + Forward, Up);
    }
    
    public Matrix4x4 GetProjectionMatrix()
    {
        if (AspectRatio <= 0)
            throw new InvalidOperationException("Aspect ratio has not been set.");
        
        return Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(Zoom), AspectRatio, _config.Near, _config.Far);
    }
}