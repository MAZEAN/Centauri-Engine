namespace SimpleTerrain.Rendering;
using System;
using Silk.NET.OpenGL;
using Core;

public class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly VertexArrayObject<float, uint> _vao;
    private readonly BufferObject<float> _vbo;
    private readonly BufferObject<uint> _ebo;

    public uint VertexCount { get; }
    public uint IndexCount { get; }

    public Mesh(GL gl, float[] vertices, uint[] indices)
    {
        _gl = gl;
        VertexCount = (uint)vertices.Length;
        IndexCount  = (uint)indices.Length;
        
        _ebo = new BufferObject<uint>(_gl, indices, BufferTargetARB.ElementArrayBuffer);
        _vbo = new BufferObject<float>(_gl, vertices, BufferTargetARB.ArrayBuffer);
        _vao = new VertexArrayObject<float, uint>(_gl, _vbo, _ebo);
        
        // stride = 11 (pos=3, normal=3, uv=2, tangent=3)
        _vao.VertexAttributePointer(0, 3, VertexAttribPointerType.Float, 11, 0);  // position
        _vao.VertexAttributePointer(1, 3, VertexAttribPointerType.Float, 11, 3);  // normal
        _vao.VertexAttributePointer(2, 2, VertexAttribPointerType.Float, 11, 6);  // uv
        _vao.VertexAttributePointer(3, 3, VertexAttribPointerType.Float, 11, 8);  // tangent
        
        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
    }

    public void Bind() => _vao.Bind();

    public void Dispose()
    {
        _vao.Dispose();
        _vbo.Dispose();
        _ebo.Dispose();
    }
}