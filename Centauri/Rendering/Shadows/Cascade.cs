namespace Centauri.Rendering.Shadows;

using System.Numerics;

// Per-cascade data produced by ShadowMapper each frame.
public struct Cascade
{
    public Matrix4x4 Matrix;      // lightView * lightProj  (GLSL: uLightMatrix * pos)
    public float     SplitDepth;  // view-space far depth — used for cascade selection
    public Vector3   Center;      // world-space slice center  (depth-pass culling, Step 2)
    public float     Radius;      // bounding-sphere radius    (depth-pass culling, Step 2)
}