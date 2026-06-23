namespace Centauri.Graphics.Resources.Buffers;

using Silk.NET.OpenGL;

// A std140 uniform buffer bound to a fixed binding point. Owns the raw GL UBO lifecycle
// (allocate once, sub-data per upload) so layout-owning types like LightBuffer don't
// repeat the gen/bind/BufferData/BindBufferBase boilerplate.
public sealed class UniformBufferObject : GLResource
{
    private readonly nuint _sizeBytes;

    public unsafe UniformBufferObject(GL gl, uint bindingPoint, nuint sizeBytes) : base(gl)
    {
        _sizeBytes = sizeBytes;
        Handle = gl.GenBuffer();

        gl.BindBuffer(BufferTargetARB.UniformBuffer, Handle);
        gl.BufferData(BufferTargetARB.UniformBuffer, sizeBytes, null, BufferUsageARB.DynamicDraw);
        gl.BindBufferBase(BufferTargetARB.UniformBuffer, bindingPoint, Handle);
        gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    // Replace the whole buffer's contents (data length must match the allocated size).
    public unsafe void Upload(ReadOnlySpan<float> data)
    {
        Gl.BindBuffer(BufferTargetARB.UniformBuffer, Handle);
        fixed (float* p = data)
            Gl.BufferSubData(BufferTargetARB.UniformBuffer, 0, _sizeBytes, p);
        Gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    protected override void DeleteGL() => Gl.DeleteBuffer(Handle);
}