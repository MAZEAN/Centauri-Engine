namespace Centauri.Rendering.Resources;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

using World.Collections;

// A single std140 uniform buffer holding all scene lights, shared by every lit shader
// (bound to BindingPoint). Every member is padded to a vec4 so the byte layout is
// deterministic across drivers — no alignment surprises.
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
    private const int TotalFloats  = DirFloats + PointFloats * MaxPoint + SpotFloats * MaxSpot + CountsFloats;
    private const int TotalBytes   = TotalFloats * sizeof(float);

    // spotlights carry no attenuation fields, so use the point-light defaults
    private const float SpotConstant  = 1.0f;
    private const float SpotLinear    = 0.09f;
    private const float SpotQuadratic = 0.032f;

    private readonly GL _gl;
    private readonly uint _handle;
    private readonly float[] _data = new float[TotalFloats];

    public unsafe LightBuffer(GL gl)
    {
        _gl = gl;
        _handle = gl.GenBuffer();

        gl.BindBuffer(BufferTargetARB.UniformBuffer, _handle);
        gl.BufferData(BufferTargetARB.UniformBuffer, (nuint)TotalBytes, null, BufferUsageARB.DynamicDraw);
        gl.BindBufferBase(BufferTargetARB.UniformBuffer, BindingPoint, _handle);
        gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    public unsafe void Update(LightingSystem lights)
    {
        Array.Clear(_data); // zero padding + unused slots

        var offset = 0;

        // ── directional (first enabled) ──────────────────────────────────────
        var hasDir = 0;
        if (lights.DirectionalLights.Count > 0)
        {
            var d = lights.DirectionalLights[0];
            WriteVec3(offset + 0, d.Direction);
            WriteVec3(offset + 4, d.Color);
            _data[offset + 8] = d.Intensity;
            hasDir = 1;
        }
        offset += DirFloats;

        // ── point lights ─────────────────────────────────────────────────────
        var pointCount = Math.Min(lights.PointLights.Count, MaxPoint);
        for (var i = 0; i < pointCount; i++)
        {
            var a = lights.PointLights[i];
            var o = offset + i * PointFloats;

            WriteVec3(o + 0, a.Position);
            WriteVec3(o + 4, a.Light.Color);
            _data[o + 8]  = a.Light.Intensity;
            _data[o + 9]  = a.Light.Constant;
            _data[o + 10] = a.Light.Linear;
            _data[o + 11] = a.Light.Quadratic;
        }
        offset += PointFloats * MaxPoint;

        // ── spotlights ───────────────────────────────────────────────────────
        var spotCount = Math.Min(lights.SpotLights.Count, MaxSpot);
        for (var i = 0; i < spotCount; i++)
        {
            var a = lights.SpotLights[i];
            var l = a.Light;
            var o = offset + i * SpotFloats;

            WriteVec3(o + 0,  a.Position);
            WriteVec3(o + 4,  l.Direction);
            WriteVec3(o + 8,  l.Color);
            _data[o + 12] = l.Intensity;
            _data[o + 13] = SpotConstant;
            _data[o + 14] = SpotLinear;
            _data[o + 15] = SpotQuadratic;
            _data[o + 16] = MathF.Cos(l.InnerCutoff * MathF.PI / 180f);
            _data[o + 17] = MathF.Cos(l.OuterCutoff * MathF.PI / 180f);
        }
        offset += SpotFloats * MaxSpot;

        // ── counts (ivec4) — written as int bits ─────────────────────────────
        var counts = MemoryMarshal.Cast<float, int>(_data.AsSpan(offset, CountsFloats));
        counts[0] = pointCount;
        counts[1] = spotCount;
        counts[2] = hasDir;
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