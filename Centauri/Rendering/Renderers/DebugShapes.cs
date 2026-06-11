namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;

using Resources;

// Pure geometry/data for debug visuals — no GL state, no drawing.
internal static class DebugShapes
{
    public const float CameraScale     =  0.5f;
    public const float CameraModelBase = -0.4f;

    // indices into BoundingBox.GetBoxCorners()
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

    public static float[] BoxEdges(Vector3[] corners) => Expand(corners, EdgeIndices);
    public static float[] BoxFaces(Vector3[] corners) => Expand(corners, FaceIndices);

    // flatten the indexed corners into a packed xyz vertex array
    private static float[] Expand(Vector3[] corners, int[] indices)
    {
        var v = new float[indices.Length * 3];
        for (int i = 0; i < indices.Length; i++)
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
            0f,    0f,  0f,  0,1,0, 0,0, 1,0,0,
            -0.2f,-0.2f,  b,  0,1,0, 0,0, 1,0,0,
            0.2f,-0.2f,  b,  0,1,0, 0,0, 1,0,0,
            0.2f, 0.2f,  b,  0,1,0, 0,0, 1,0,0,
            -0.2f, 0.2f,  b,  0,1,0, 0,0, 1,0,0,
        ];
        uint[] indices = [0,1,2, 0,2,3, 0,3,4, 0,4,1, 1,2,3, 3,4,1];
        return new Mesh(gl, vertices, indices);
    }
}