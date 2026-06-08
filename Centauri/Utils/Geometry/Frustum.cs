namespace Centauri.Utils.Geometry;

using System.Numerics;
using Plane = System.Numerics.Plane;

using Math;
using Config;
using World;

public class Frustum
{
    private readonly Camera _camera;
    private readonly CameraConfig _config;

    private Plane[] Planes { get; } = new Plane[6];
    
    public Frustum(Camera camera, CameraConfig config)
    {
        _camera = camera;
        _config = config;
    }

    public void BuildFrustumPlanes()
    {
        if (!_camera.IsFrustumDirty) return;
        
        var vp = _camera.GetViewMatrix() * _camera.GetProjectionMatrix();

        Planes[0] = CreatePlane(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41); // left
        Planes[1] = CreatePlane(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41); // right
        Planes[2] = CreatePlane(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42); // bottom
        Planes[3] = CreatePlane(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42); // top
        Planes[4] = CreatePlane(vp.M13, vp.M23, vp.M33, vp.M43);                                     // near
        Planes[5] = CreatePlane(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43); // far
        
        _camera.ClearFrustumDirty();
    }

    private static Plane CreatePlane(float a, float b, float c, float d)
    {
        var normal = new Vector3(a, b, c);
        var length = normal.Length();

        return new Plane(normal / length, d / length);
    }
    
    // Only used for visualization
    public Vector3[] GetFrustumCorners()
    {
        var fov = MathHelper.DegreesToRadians(_camera.Zoom);
        var tanFov = MathF.Tan(fov / 2f);

        var near = _config.Near;
        var far  = _config.Far;

        var nearHeight = 2f * tanFov * near;
        var nearWidth  = nearHeight * _camera.AspectRatio;

        var farHeight = 2f * tanFov * far;
        var farWidth  = farHeight * _camera.AspectRatio;

        var forward = _camera.Forward;
        var right   = _camera.Right;
        var up      = _camera.Up;

        var nearCenter = _camera.Position + forward * near;
        var farCenter  = _camera.Position + forward * far;

        var corners = new Vector3[8];

        // near plane
        corners[0] = nearCenter + up * (nearHeight * 0.5f) - right * (nearWidth * 0.5f);
        corners[1] = nearCenter + up * (nearHeight * 0.5f) + right * (nearWidth * 0.5f);
        corners[2] = nearCenter - up * (nearHeight * 0.5f) - right * (nearWidth * 0.5f);
        corners[3] = nearCenter - up * (nearHeight * 0.5f) + right * (nearWidth * 0.5f);

        // far plane
        corners[4] = farCenter + up * (farHeight * 0.5f) - right * (farWidth * 0.5f);
        corners[5] = farCenter + up * (farHeight * 0.5f) + right * (farWidth * 0.5f);
        corners[6] = farCenter - up * (farHeight * 0.5f) - right * (farWidth * 0.5f);
        corners[7] = farCenter - up * (farHeight * 0.5f) + right * (farWidth * 0.5f);

        return corners;
    }
    
    public bool IsVisibleAABB(BoundingBox box)
    {
        foreach (var plane in Planes)
        {
            var normal = plane.Normal;

            var p = new Vector3(
                normal.X >= 0 ? box.Max.X : box.Min.X,
                normal.Y >= 0 ? box.Max.Y : box.Min.Y,
                normal.Z >= 0 ? box.Max.Z : box.Min.Z
            );

            if (Vector3.Dot(normal, p) + plane.D < 0)
                return false;
        }
        return true;
    }
}