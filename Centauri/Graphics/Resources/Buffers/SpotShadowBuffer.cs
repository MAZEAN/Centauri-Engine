namespace Centauri.Graphics.Resources.Buffers;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

// The std140 layout for spot-light shadow matrices, shared by every lit shader (bound to
// BindingPoint — Lights occupies 0, Shadows (CSM) occupies 1). One mat4 per SpotShadowConfig
// .MaxShadowSpots atlas layer; which SpotLight (if any) currently owns a given layer is carried
// separately, packed into that light's own Lights-UBO entry (SpotLight.cutoffs.z — see
// LightBuffer.AddSpot) rather than duplicated here.
public sealed class SpotShadowBuffer : IDisposable
{
    public const uint BindingPoint = 2;

    private readonly int _maxSlots;
    private readonly int _totalFloats;

    private readonly UniformBufferObject _ubo;
    private readonly float[] _data;

    public SpotShadowBuffer(GL gl, int maxSlots)
    {
        _maxSlots    = maxSlots;
        _totalFloats = maxSlots * 16;
        _data        = new float[_totalFloats];

        _ubo = new UniformBufferObject(gl, BindingPoint, (nuint)(_totalFloats * sizeof(float)));
    }

    public void SetSlot(int index, Matrix4x4 matrix)
    {
        if ((uint)index >= (uint)_maxSlots) return;
        MemoryMarshal.Cast<float, Matrix4x4>(_data.AsSpan(index * 16, 16))[0] = matrix;
    }

    public void Upload() => _ubo.Upload(_data);

    public void Dispose() => _ubo.Dispose();
}
