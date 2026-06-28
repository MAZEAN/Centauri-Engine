namespace Centauri.Utils.Geometry;

using System.Numerics;

public readonly struct BoundingBox
{
    public Vector3 Min     { get; }
    public Vector3 Max     { get; }
    public Vector3 Center  { get; }
    public Vector3 Extents { get; }

    public BoundingBox(Vector3 min, Vector3 max)
    {
        Min     = min;
        Max     = max;
        Center  = (min + max) * 0.5f;
        Extents = (max - min) * 0.5f;
    }
    
    public BoundingBox Transform(Matrix4x4 m)
    {
        var center = Vector3.Transform(Center, m);

        var right = new Vector3(m.M11, m.M12, m.M13) * Extents.X;
        var up    = new Vector3(m.M21, m.M22, m.M23) * Extents.Y;
        var forward = new Vector3(m.M31, m.M32, m.M33) * Extents.Z;

        var newExtents = new Vector3(
            MathF.Abs(right.X) + MathF.Abs(up.X) + MathF.Abs(forward.X),
            MathF.Abs(right.Y) + MathF.Abs(up.Y) + MathF.Abs(forward.Y),
            MathF.Abs(right.Z) + MathF.Abs(up.Z) + MathF.Abs(forward.Z)
        );

        return new BoundingBox(center - newExtents, center + newExtents);
    }
    
    public Vector3[] GetBoxCorners() =>
    [
        new(Min.X, Min.Y, Min.Z), // 0 left  bottom back
        new(Max.X, Min.Y, Min.Z), // 1 right bottom back
        new(Min.X, Max.Y, Min.Z), // 2 left  top    back
        new(Max.X, Max.Y, Min.Z), // 3 right top    back
        new(Min.X, Min.Y, Max.Z), // 4 left  bottom front
        new(Max.X, Min.Y, Max.Z), // 5 right bottom front
        new(Min.X, Max.Y, Max.Z), // 6 left  top    front
        new(Max.X, Max.Y, Max.Z), // 7 right top    front
    ];
    
    public bool Contains(Vector3 point) =>
        point.X >= Min.X && point.X <= Max.X &&
        point.Y >= Min.Y && point.Y <= Max.Y &&
        point.Z >= Min.Z && point.Z <= Max.Z;

    public bool Intersects(BoundingBox other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
        Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    
    public bool Intersects(Ray ray, out float t)
    {
        var invD = new Vector3(1f / ray.Direction.X, 1f / ray.Direction.Y, 1f / ray.Direction.Z);

        var t0 = (Min - ray.Origin) * invD;
        var t1 = (Max - ray.Origin) * invD;

        var tMin = Vector3.Min(t0, t1);
        var tMax = Vector3.Max(t0, t1);

        var near = MathF.Max(MathF.Max(tMin.X, tMin.Y), tMin.Z);
        var far  = MathF.Min(MathF.Min(tMax.X, tMax.Y), tMax.Z);

        t = near;
        return far >= MathF.Max(near, 0f); // hit, and in front of the ray
    }
    
}