namespace Centauri.Utils.Geometry;

using System.Numerics;
using Plane = System.Numerics.Plane;

// A view-frustum (or any view*proj) volume: 6 planes + AABB test.
// Decoupled from Camera so cascades, probes, etc. can reuse it.
public sealed class Frustum
{
    private readonly Plane[] _planes = new Plane[6];

    // Rebuild the 6 planes from a combined view*proj matrix (Gribb-Hartmann).
    public void Update(Matrix4x4 vp)
    {
        _planes[0] = CreatePlane(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41); // left
        _planes[1] = CreatePlane(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41); // right
        _planes[2] = CreatePlane(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42); // bottom
        _planes[3] = CreatePlane(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42); // top
        _planes[4] = CreatePlane(vp.M13,          vp.M23,          vp.M33,          vp.M43);           // near
        _planes[5] = CreatePlane(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43); // far
    }

    public bool IsVisibleAABB(BoundingBox box)
    {
        foreach (var plane in _planes)
        {
            var n = plane.Normal;
            var p = new Vector3(
                n.X >= 0 ? box.Max.X : box.Min.X,
                n.Y >= 0 ? box.Max.Y : box.Min.Y,
                n.Z >= 0 ? box.Max.Z : box.Min.Z);

            if (Vector3.Dot(n, p) + plane.D < 0)
                return false;
        }
        return true;
    }

    private static Plane CreatePlane(float a, float b, float c, float d)
    {
        var normal = new Vector3(a, b, c);
        var length = normal.Length();
        return new Plane(normal / length, d / length);
    }
}