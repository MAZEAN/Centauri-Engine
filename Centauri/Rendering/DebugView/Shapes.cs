namespace Centauri.Rendering.DebugView;

using Silk.NET.OpenGL;
using System.Numerics;

using Graphics.Geometry;

// Pure geometry/data for debug visuals — no GL state, no drawing.
internal static class Shapes
{
    public const float CameraScale     =  0.5f;
    public const float CameraModelBase = -0.4f;
    
    private static readonly int[] EdgeIndices =
    [
        0,1, 1,3, 3,2, 2,0,  // back face
        4,5, 5,7, 7,6, 6,4,  // front face
        0,4, 1,5, 2,6, 3,7   // connecting edges
    ];

    private static readonly int[] FaceIndices =
    [
        0,1,3, 0,3,2,  // back   (z = min)
        4,5,7, 4,7,6,  // front  (z = max)
        0,2,6, 0,6,4,  // left   (x = min)
        1,3,7, 1,7,5,  // right  (x = max)
        0,1,5, 0,5,4,  // bottom (y = min)
        2,3,7, 2,7,6   // top    (y = max)
    ];

    public static float[] BoxEdges(ReadOnlySpan<Vector3> corners) => Expand(corners, EdgeIndices);
    public static float[] BoxFaces(ReadOnlySpan<Vector3> corners) => Expand(corners, FaceIndices);

    // Three orthogonal great circles (XY/XZ/YZ), local space and unrotated — a sphere reads the
    // same from every angle, so unlike BoxEdges this needs no per-instance corner computation;
    // callers just translate a Model matrix to the collider's world centre. GL_LINES layout
    // (Draw.Lines), same as BoxEdges.
    public static float[] SphereEdges(float radius, int segments = 24)
    {
        var v = new float[segments * 2 * 3 * 3]; // 3 circles * segments segments * 2 verts * 3 floats
        var i = 0;

        void Circle(Func<float, Vector3> point)
        {
            for (var s = 0; s < segments; s++)
            {
                var p0 = point(MathF.Tau * s       / segments);
                var p1 = point(MathF.Tau * (s + 1) / segments);
                v[i++] = p0.X; v[i++] = p0.Y; v[i++] = p0.Z;
                v[i++] = p1.X; v[i++] = p1.Y; v[i++] = p1.Z;
            }
        }

        Circle(a => new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0f));
        Circle(a => new Vector3(MathF.Cos(a) * radius, 0f, MathF.Sin(a) * radius));
        Circle(a => new Vector3(0f, MathF.Cos(a) * radius, MathF.Sin(a) * radius));

        return v;
    }

    // Local-space, unrotated (like SphereEdges — caller translates+rotates a Model matrix to the
    // collider's world pose): two horizontal rings at the cylinder's ±halfLength ends, four
    // straight lines connecting them (the cylinder's silhouette), and two half-circles per end
    // (in the XY and ZY planes) sketching the hemispherical caps. Not a full 3D capsule mesh —
    // same "wireframe good enough to read the shape from any angle" bar SphereEdges/BoxEdges set,
    // not a render-quality approximation.
    public static float[] CapsuleEdges(float radius, float halfLength, int segments = 16)
    {
        var lines = new List<float>();

        void Line(Vector3 a, Vector3 b)
        {
            lines.Add(a.X); lines.Add(a.Y); lines.Add(a.Z);
            lines.Add(b.X); lines.Add(b.Y); lines.Add(b.Z);
        }

        void Ring(float y)
        {
            for (var s = 0; s < segments; s++)
            {
                var a0 = MathF.Tau * s       / segments;
                var a1 = MathF.Tau * (s + 1) / segments;
                Line(new Vector3(MathF.Cos(a0) * radius, y, MathF.Sin(a0) * radius),
                     new Vector3(MathF.Cos(a1) * radius, y, MathF.Sin(a1) * radius));
            }
        }

        // Half-circle from angle 0 to π in the given plane, centred at (0, ±halfLength, 0) so it
        // caps the cylinder's end rather than passing back through its middle.
        void HalfCircle(float capY, float sign, Func<float, Vector3> point)
        {
            for (var s = 0; s < segments; s++)
            {
                var a0 = MathF.PI * s       / segments;
                var a1 = MathF.PI * (s + 1) / segments;
                var p0 = point(a0);
                var p1 = point(a1);
                Line(new Vector3(p0.X, capY + sign * p0.Y, p0.Z), new Vector3(p1.X, capY + sign * p1.Y, p1.Z));
            }
        }

        Ring(halfLength);
        Ring(-halfLength);

        Line(new Vector3(radius, halfLength, 0f),  new Vector3(radius, -halfLength, 0f));
        Line(new Vector3(-radius, halfLength, 0f), new Vector3(-radius, -halfLength, 0f));
        Line(new Vector3(0f, halfLength, radius),  new Vector3(0f, -halfLength, radius));
        Line(new Vector3(0f, halfLength, -radius), new Vector3(0f, -halfLength, -radius));

        HalfCircle(halfLength, 1f, a => new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0f));
        HalfCircle(halfLength, 1f, a => new Vector3(0f, MathF.Sin(a) * radius, MathF.Cos(a) * radius));
        HalfCircle(-halfLength, -1f, a => new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0f));
        HalfCircle(-halfLength, -1f, a => new Vector3(0f, MathF.Sin(a) * radius, MathF.Cos(a) * radius));

        return lines.ToArray();
    }

    private static float[] Expand(ReadOnlySpan<Vector3> corners, int[] indices)
    {
        var v = new float[indices.Length * 3];
        for (var i = 0; i < indices.Length; i++)
        {
            var p = corners[indices[i]];
            v[i * 3 + 0] = p.X;
            v[i * 3 + 1] = p.Y;
            v[i * 3 + 2] = p.Z;
        }
        return v;
    }

    public static Mesh BuildCameraMesh(GL gl)
    {
        const float b = CameraModelBase;
        float[] vertices =
        [
             0f,      0f, 0f,  0f, 1f, 0f,  0f, 0f, 1f,  0f, 0f,
            -0.2f, -0.2f, b,   0f, 1f, 0f,  0f, 0f, 1f,  0f, 0f,
             0.2f, -0.2f, b,   0f, 1f, 0f,  0f, 0f, 1f,  0f, 0f,
             0.2f,  0.2f, b,   0f, 1f, 0f,  0f, 0f, 1f,  0f, 0f,
            -0.2f,  0.2f, b,   0f, 1f, 0f,  0f, 0f, 1f,  0f, 0f,
        ];
        
        uint[] indices = [0, 1, 2,  0, 2, 3,  0, 3, 4,  0, 4, 1,  1, 2, 3,  3, 4, 1];
        
        return new Mesh(gl, vertices, indices);
    }
}