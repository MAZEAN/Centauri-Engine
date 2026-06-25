namespace Centauri.Graphics.Geometry;

using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

// Per-instance vertex data streamed to the GPU for instanced draws: the world matrix
// (consumed as a mat4 across attribute locations 4–7) plus UV tiling packed into a vec4
// (location 8). Layout must match Mesh.ConfigureInstancing and shaderPBR.vert.
[StructLayout(LayoutKind.Sequential)]
public readonly struct InstanceData
{
    public const int Floats = 20;               // mat4 (16) + vec4 (4)
    public const int Bytes  = Floats * sizeof(float);

    public readonly Matrix4x4 Model;
    public readonly Vector4   UvScaleOffset;     // xy = scale, zw = offset

    public InstanceData(Matrix4x4 model, Vector2 uvScale, Vector2 uvOffset)
    {
        Model         = model;
        UvScaleOffset = new Vector4(uvScale.X, uvScale.Y, uvOffset.X, uvOffset.Y);
    }
}

// A single GPU buffer reused for every instanced batch in a frame. Each batch orphans and
// refills it just before its draw — the orphan lets the driver hand back fresh storage
// instead of stalling on the previous batch's in-flight read. Grows on demand.
public sealed class InstanceBuffer : IDisposable
{
    private readonly GL   _gl;
    private readonly uint _handle;
    private int           _capacityBytes;

    public uint Handle => _handle;

    public unsafe InstanceBuffer(GL gl, int initialInstances = 256)
    {
        _gl            = gl;
        _handle        = _gl.GenBuffer();
        _capacityBytes = initialInstances * InstanceData.Bytes;

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _handle);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)_capacityBytes, null, BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    public unsafe void Upload(List<InstanceData> instances)
    {
        var bytes = instances.Count * InstanceData.Bytes;
        var span  = CollectionsMarshal.AsSpan(instances);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _handle);

        fixed (InstanceData* p = span)
        {
            if (bytes > _capacityBytes)
            {
                _capacityBytes = bytes;
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)bytes, p, BufferUsageARB.DynamicDraw);
            }
            else
            {
                // orphan the old store, then refill — avoids a sync with the prior draw
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)_capacityBytes, null, BufferUsageARB.DynamicDraw);
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)bytes, p);
            }
        }
    }

    public void Dispose() => _gl.DeleteBuffer(_handle);
}