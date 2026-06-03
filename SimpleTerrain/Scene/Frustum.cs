namespace SimpleTerrain.Scene;

using System.Numerics;
using Plane = System.Numerics.Plane;

using Utils;

public class Frustum
{
    public Plane[] Planes { get; } = new Plane[6];

    public Frustum(Matrix4x4 view, Matrix4x4 projection)
    {
        var vp = view * projection;

        Planes[0] = CreatePlane(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41); // left
        Planes[1] = CreatePlane(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41); // right
        Planes[2] = CreatePlane(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42); // bottom
        Planes[3] = CreatePlane(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42); // top
        Planes[4] = CreatePlane(vp.M13, vp.M23, vp.M33, vp.M43);                                     // near
        Planes[5] = CreatePlane(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43); // far
    }

    private static Plane CreatePlane(float a, float b, float c, float d)
    {
        var normal = new Vector3(a, b, c);
        float length = normal.Length();

        return new Plane(normal / length, d / length);
    }
    
    public static Vector3[] GetFrustumCorners(Camera camera)
    {
        float fov = MathHelper.DegreesToRadians(camera.Zoom);
        float tanFov = MathF.Tan(fov / 2f);

        float near = 0.1f;
        float far  = 100f;

        float nearHeight = 2f * tanFov * near;
        float nearWidth  = nearHeight * camera.AspectRatio;

        float farHeight = 2f * tanFov * far;
        float farWidth  = farHeight * camera.AspectRatio;

        Vector3 forward = camera.Forward;
        Vector3 right   = camera.Right;
        Vector3 up      = camera.Up;

        Vector3 nearCenter = camera.Position + forward * near;
        Vector3 farCenter  = camera.Position + forward * far;

        Vector3[] corners = new Vector3[8];

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
}