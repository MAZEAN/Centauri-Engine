namespace Centauri.Graphics.Resources.Buffers;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

// The std140 layout for the CSM data shared by every lit shader (bound to BindingPoint).
// Holds the heavy, per-frame-constant arrays — the light matrices and the per-cascade
// split / texel sizes — so they upload once a frame instead of once per shader switch.
// The cheap scalar shadow params (bias, pcf radius, counts) stay as loose uniforms.
//
//   mat4  uLightMatrices[4]  = 4 * 64 B = 256 B   (raw Matrix4x4 bytes; GL reads them
//                                                   column-major, matching UniformMatrix4
//                                                   with transpose=false)
//   vec4  uCascadeSplits     = 16 B               (4 cascade far depths packed x..w)
//   vec4  uTexelWorld        = 16 B               (4 world texel sizes packed x..w)
public sealed class ShadowBuffer : IDisposable
{
    public const uint BindingPoint = 1;   // Lights occupies 0

    private const int MaxCascades    = 4;
    private const int MatricesFloats = MaxCascades * 16;   // 64
    private const int SplitsBase     = MatricesFloats;      // 64
    private const int TexelBase      = SplitsBase + 4;      // 68
    private const int TotalFloats    = TexelBase + 4;       // 72
    private const int TotalBytes     = TotalFloats * sizeof(float);

    private readonly UniformBufferObject _ubo;
    private readonly float[] _data = new float[TotalFloats];

    public ShadowBuffer(GL gl) => _ubo = new UniformBufferObject(gl, BindingPoint, (nuint)TotalBytes);

    // Pack one cascade's matrix plus its split depth and world texel size. The vec4 packing
    // of splits/texel assumes at most MaxCascades (4) cascades — one component each.
    public void SetCascade(int index, Matrix4x4 matrix, float splitDepth, float texelWorld)
    {
        MemoryMarshal.Cast<float, Matrix4x4>(_data.AsSpan(index * 16, 16))[0] = matrix;
        _data[SplitsBase + index] = splitDepth;
        _data[TexelBase  + index] = texelWorld;
    }

    public void Upload() => _ubo.Upload(_data);

    public void Dispose() => _ubo.Dispose();
}
