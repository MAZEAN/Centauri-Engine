namespace Centauri.Rendering.Resources;

using Silk.NET.OpenGL;
using System.Numerics;
using Core;
using Utils.Geometry;

public class Mesh : IDisposable
{
    private const uint Stride = 11; // pos(3) + normal(3) + uv(2) + tangent(3)

    private readonly GL _gl;
    private readonly VertexArrayObject<float, uint> _vao;
    private readonly BufferObject<float>            _vbo;
    private readonly BufferObject<uint>             _ebo;

    public uint        VertexCount { get; }
    public uint        IndexCount  { get; }
    public BoundingBox Bounds      { get; }

    public Mesh(GL gl, float[] vertices, uint[] indices)
    {
        _gl = gl;

        // correct vertex count — total floats / floats per vertex
        VertexCount = (uint)vertices.Length / Stride;
        IndexCount  = (uint)indices.Length;
        Bounds      = ComputeBounds(vertices);

        _ebo = new BufferObject<uint>(_gl,  indices,  BufferTargetARB.ElementArrayBuffer);
        _vbo = new BufferObject<float>(_gl, vertices, BufferTargetARB.ArrayBuffer);
        _vao = new VertexArrayObject<float, uint>(_gl, _vbo, _ebo);

        _vao.VertexAttributePointer(0, 3, VertexAttribPointerType.Float, Stride, 0);  // position
        _vao.VertexAttributePointer(1, 3, VertexAttribPointerType.Float, Stride, 3);  // normal
        _vao.VertexAttributePointer(2, 2, VertexAttribPointerType.Float, Stride, 6);  // uv
        _vao.VertexAttributePointer(3, 3, VertexAttribPointerType.Float, Stride, 8);  // tangent

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer,        0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
    }

    private static BoundingBox ComputeBounds(float[] vertices)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        const int stride = (int)Stride;

        for (var i = 0; i < vertices.Length; i += stride)
        {
            var pos = new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]);
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
        }

        return new BoundingBox(min, max);
    }

    public void Bind() => _vao.Bind();

    public void Dispose()
    {
        _vao.Dispose();
        _vbo.Dispose();
        _ebo.Dispose();
    }
}