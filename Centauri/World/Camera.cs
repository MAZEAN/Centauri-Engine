namespace Centauri.World;

using System.Numerics;
using Silk.NET.Maths;

using Config;
using Utils.Math;
using Utils.Geometry;

public class Camera
{
    private readonly CameraConfig _config;
    public string Name       { get; }
    public Vector3 Position  { get; private set; }
    public Vector3 Forward   { get; private set; }
    public Vector3 Right     { get; private set; }
    public Vector3 Up        { get; private set; }
    public Vector3 WorldUp   { get; }
    public float Yaw         { get; private set; }
    public float Pitch       { get; private set; }
    public float Zoom        { get; private set; }
    public float AspectRatio { get; private set; }

    public Frustum Frustum { get; private set; } = new();
    private bool IsFrustumDirty { get; set; } = true;

    public Camera(CameraConfig config, string name, Vector3 position, Vector3 worldUp, float yaw, float pitch)
    {
        _config = config;
        Name = name;

        Position = position;
        WorldUp = worldUp;

        Yaw = yaw;
        Pitch = pitch;
        Zoom = config.FOV;

        UpdateVectors();
    }
    
    public void UpdatePosition(Vector3 delta)
    {
        Position += delta;
        IsFrustumDirty = true;
    }
    
    public void ModifyDirection(float xOffset, float yOffset)
    {
        Yaw   += xOffset;
        Pitch += -yOffset;

        // clamp pitch
        Pitch = Math.Clamp(Pitch, -89f, 89f);

        UpdateVectors();
        IsFrustumDirty = true;
    }

    public void AdjustZoom(float zoomDelta)
    {
        Zoom = Math.Clamp(Zoom + zoomDelta, _config.MinZoom, _config.MaxZoom);
        IsFrustumDirty = true;
    }
    
    private void UpdateVectors()
    {
        var yawRad = MathHelper.DegreesToRadians(Yaw);
        var pitchRad = MathHelper.DegreesToRadians(Pitch);

        var direction = new Vector3(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad)
        );

        Forward = Vector3.Normalize(direction);
        
        Right = Vector3.Normalize(Vector3.Cross(Forward, WorldUp));
        Up    = Vector3.Normalize(Vector3.Cross(Right, Forward));
    }
    
    public void SetAspectRatio(Vector2D<int> newSize)
    {
        if (newSize.Y <= 0)
            throw new ArgumentException("Height must be positive.");
        AspectRatio = (float)newSize.X / newSize.Y;
        IsFrustumDirty = true;
    }
    
    public Ray ScreenPointToRay(Vector2 screen, Vector2 viewport)
    {
        var ndcX = 2f * screen.X / viewport.X - 1f;
        var ndcY = 1f - 2f * screen.Y / viewport.Y; // screen Y is down, NDC Y is up

        Matrix4x4.Invert(GetViewMatrix() * GetProjectionMatrix(), out var invVP);

        var near = Unproject(new Vector3(ndcX, ndcY, 0f), invVP); // .NET projection: near z = 0
        var far  = Unproject(new Vector3(ndcX, ndcY, 1f), invVP); // far z = 1

        return new Ray(near, far - near);
    }

    private static Vector3 Unproject(Vector3 ndc, Matrix4x4 invVP)
    {
        var p = Vector4.Transform(new Vector4(ndc, 1f), invVP);
        return new Vector3(p.X, p.Y, p.Z) / p.W;
    }
    
    public void UpdateFrustum()
    {
        if (!IsFrustumDirty) return;
        Frustum.Update(GetViewMatrix() * GetProjectionMatrix());
        IsFrustumDirty = false;
    }
    
    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Position + Forward, Up);
    }
    
    public Vector2 JitterNdc { get; set; } = Vector2.Zero;
    
    public Matrix4x4 GetProjectionMatrix()
    {
        var proj = GetProjectionMatrixRaw();
        proj.M31 += JitterNdc.X;   // shifts clip.x by jitter*w → constant NDC offset after divide
        proj.M32 += JitterNdc.Y;
        return proj;
    }
    
    public Matrix4x4 GetProjectionMatrixRaw()
    {
        if (AspectRatio <= 0)
            throw new InvalidOperationException("Aspect ratio has not been set.");
        
        return Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(Zoom), AspectRatio, _config.Near, _config.Far);
    }
    
    public Vector3[] GetFrustumCorners()
    {
        var corners = new Vector3[8];
        GetFrustumCorners(corners);
        return corners;
    }

    public void GetFrustumCorners(Span<Vector3> dest)
    {
        var tanFov = MathF.Tan(MathHelper.DegreesToRadians(Zoom) / 2f);

        var near = _config.Near;
        var far  = _config.Far;

        var nearHeight = 2f * tanFov * near;
        var nearWidth  = nearHeight * AspectRatio;
        var farHeight  = 2f * tanFov * far;
        var farWidth   = farHeight * AspectRatio;
        
        var nearCenter = Position + Forward * near;
        var farCenter  = Position + Forward * far;
        
        dest[0] = nearCenter + Up * (nearHeight * 0.5f) - Right * (nearWidth * 0.5f);
        dest[1] = nearCenter + Up * (nearHeight * 0.5f) + Right * (nearWidth * 0.5f);
        dest[2] = nearCenter - Up * (nearHeight * 0.5f) - Right * (nearWidth * 0.5f);
        dest[3] = nearCenter - Up * (nearHeight * 0.5f) + Right * (nearWidth * 0.5f);
        dest[4] = farCenter  + Up * (farHeight  * 0.5f) - Right * (farWidth  * 0.5f);
        dest[5] = farCenter  + Up * (farHeight  * 0.5f) + Right * (farWidth  * 0.5f);
        dest[6] = farCenter  - Up * (farHeight  * 0.5f) - Right * (farWidth  * 0.5f);
        dest[7] = farCenter  - Up * (farHeight  * 0.5f) + Right * (farWidth  * 0.5f);
    }
}