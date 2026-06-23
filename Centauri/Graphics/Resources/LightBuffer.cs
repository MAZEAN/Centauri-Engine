namespace Centauri.Graphics.Resources;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

// The std140 layout for all scene lights, shared by every lit shader (bound to
// BindingPoint). Owns only the layout + write API — the GL buffer lifecycle lives in
// UniformBufferObject, and per-slot packing in Std140Writer. Knows nothing about the
// scene/light types; callers push values via Begin() / Set*/Add* / Upload().
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

    private const int PointsBase = DirFloats;                          // 12
    private const int SpotsBase  = DirFloats + PointFloats * MaxPoint; // 204
    private const int CountsBase = SpotsBase + SpotFloats * MaxSpot;   // 524
    private const int TotalFloats = CountsBase + CountsFloats;         // 528
    private const int TotalBytes  = TotalFloats * sizeof(float);

    private readonly UniformBufferObject _ubo;
    private readonly float[] _data = new float[TotalFloats];

    private int _pointCount;
    private int _spotCount;
    private int _hasDir;

    public LightBuffer(GL gl) => _ubo = new UniformBufferObject(gl, BindingPoint, (nuint)TotalBytes);

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
        var w = new Std140Writer(_data);
        w.Vec3(direction);
        w.Vec3(color);
        w.Vec4(intensity, 0f, 0f, 0f);
        _hasDir = 1;
    }

    public void AddPoint(Vector3 position, Vector3 color, float intensity,
                         float constant, float linear, float quadratic)
    {
        if (_pointCount >= MaxPoint) return;

        var w = new Std140Writer(_data, PointsBase + _pointCount * PointFloats);
        w.Vec3(position);
        w.Vec3(color);
        w.Vec4(intensity, constant, linear, quadratic);
        _pointCount++;
    }

    public void AddSpot(Vector3 position, Vector3 direction, Vector3 color, float intensity,
                        float constant, float linear, float quadratic,
                        float innerCutoffDeg, float outerCutoffDeg)
    {
        if (_spotCount >= MaxSpot) return;

        var w = new Std140Writer(_data, SpotsBase + _spotCount * SpotFloats);
        w.Vec3(position);
        w.Vec3(direction);
        w.Vec3(color);
        w.Vec4(intensity, constant, linear, quadratic);
        w.Vec4(MathF.Cos(innerCutoffDeg * MathF.PI / 180f),
               MathF.Cos(outerCutoffDeg * MathF.PI / 180f), 0f, 0f);
        _spotCount++;
    }

    public void Upload()
    {
        var counts = MemoryMarshal.Cast<float, int>(_data.AsSpan(CountsBase, CountsFloats));
        counts[0] = _pointCount;
        counts[1] = _spotCount;
        counts[2] = _hasDir;
        counts[3] = 0;

        _ubo.Upload(_data);
    }

    public void Dispose() => _ubo.Dispose();
}