namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

// A single std140 uniform buffer holding all scene lights, shared by every lit shader
// (bound to BindingPoint). Every member is padded to a vec4 so the byte layout is
// deterministic across drivers — no alignment surprises.
//
// Pure GPU layout — knows nothing about scene/light types. Callers push values via
// Begin() / SetDirectional() / AddPoint() / AddSpot() / Upload().
//
//   DirLight   : direction, color, params                  = 3 vec4   (48 B)
//   PointLight : position, color, params                   = 3 vec4   (48 B)  x16
//   SpotLight  : position, direction, color, params, cut   = 5 vec4   (80 B)  x16
//   ivec4 counts (point, spot, hasDir, _)                  = 1 vec4   (16 B)
public sealed class LightBuffer : IDisposable
{
    public const uint BindingPoint = 0;

    private const int MaxPoint = 16;
    private const int MaxSpot  = 16;

    private const int DirFloats    = 12;             // 3 vec4
    private const int PointFloats  = 12;             // 3 vec4
    private const int SpotFloats   = 20;             // 5 vec4
    private const int CountsFloats = 4;              // ivec4

    private const int PointsBase = DirFloats;                       // 12
    private const int SpotsBase  = DirFloats + PointFloats * MaxPoint;  // 204
    private const int CountsBase = SpotsBase + SpotFloats * MaxSpot;    // 524
    private const int TotalFloats = CountsBase + CountsFloats;          // 528
    private const int TotalBytes  = TotalFloats * sizeof(float);

    private readonly GL _gl;
    private readonly uint _handle;
    private readonly float[] _data = new float[TotalFloats];

    private int _pointCount;
    private int _spotCount;
    private int _hasDir;

    public unsafe LightBuffer(GL gl)
    {
        _gl = gl;
        _handle = gl.GenBuffer();

        gl.BindBuffer(BufferTargetARB.UniformBuffer, _handle);
        gl.BufferData(BufferTargetARB.UniformBuffer, (nuint)TotalBytes, null, BufferUsageARB.DynamicDraw);
        gl.BindBufferBase(BufferTargetARB.UniformBuffer, BindingPoint, _handle);
        gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    // ── write API (begin → push → upload) ───────────────────────────────────────
    public void Begin()
    {
        Array.Clear(_data);   // zero padding + unused slots
        _pointCount = 0;
        _spotCount  = 0;
        _hasDir     = 0;
    }

    public void SetDirectional(Vector3 direction, Vector3 color, float intensity)
    {
        WriteVec3(0, direction);
        WriteVec3(4, color);
        _data[8] = intensity;
        _hasDir  = 1;
    }

    public void AddPoint(Vector3 position, Vector3 color, float intensity,
                         float constant, float linear, float quadratic)
    {
        if (_pointCount >= MaxPoint) return;

        var o = PointsBase + _pointCount * PointFloats;
        WriteVec3(o + 0, position);
        WriteVec3(o + 4, color);
        _data[o + 8]  = intensity;
        _data[o + 9]  = constant;
        _data[o + 10] = linear;
        _data[o + 11] = quadratic;
        _pointCount++;
    }

    public void AddSpot(Vector3 position, Vector3 direction, Vector3 color, float intensity,
                        float constant, float linear, float quadratic,
                        float innerCutoffDeg, float outerCutoffDeg)
    {
        if (_spotCount >= MaxSpot) return;

        var o = SpotsBase + _spotCount * SpotFloats;
        WriteVec3(o + 0, position);
        WriteVec3(o + 4, direction);
        WriteVec3(o + 8, color);
        _data[o + 12] = intensity;
        _data[o + 13] = constant;
        _data[o + 14] = linear;
        _data[o + 15] = quadratic;
        _data[o + 16] = MathF.Cos(innerCutoffDeg * MathF.PI / 180f);
        _data[o + 17] = MathF.Cos(outerCutoffDeg * MathF.PI / 180f);
        _spotCount++;
    }

    public unsafe void Upload()
    {
        var counts = MemoryMarshal.Cast<float, int>(_data.AsSpan(CountsBase, CountsFloats));
        counts[0] = _pointCount;
        counts[1] = _spotCount;
        counts[2] = _hasDir;
        counts[3] = 0;

        _gl.BindBuffer(BufferTargetARB.UniformBuffer, _handle);
        fixed (float* p = _data)
            _gl.BufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)TotalBytes, p);
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    private void WriteVec3(int floatOffset, Vector3 v)
    {
        _data[floatOffset + 0] = v.X;
        _data[floatOffset + 1] = v.Y;
        _data[floatOffset + 2] = v.Z;
    }

    public void Dispose() => _gl.DeleteBuffer(_handle);
}