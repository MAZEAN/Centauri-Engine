namespace Centauri.Graphics.Geometry;

using Silk.NET.OpenGL;
using System.Numerics;


public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Tangent;
    public Vector3 Bitangent;
    public Vector2 TexCoords;
}

public class BufferObject<TDataType> : IDisposable 
    where TDataType : unmanaged
{
    private readonly uint _handle;
    private readonly BufferTargetARB _bufferType;
    private readonly GL _gl;

    public unsafe BufferObject(GL gl, Span<TDataType> data, BufferTargetARB bufferType)
    {
        _gl = gl;
        _bufferType = bufferType;
        _handle = _gl.GenBuffer();
        
        Bind();
        fixed (void* d = data)
        {
            _gl.BufferData(bufferType, (nuint) (data.Length * sizeof(TDataType)), d, BufferUsageARB.StaticDraw);
        }
    }

    public void Bind()
    {
        _gl.BindBuffer(_bufferType, _handle);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_handle);
    }
}

public class VertexArrayObject<TVertexType, TIndexType> : IDisposable
    where TVertexType : unmanaged
    where TIndexType : unmanaged
{
    private readonly uint _handle;
    private readonly GL _gl;

    public VertexArrayObject(GL gl, BufferObject<TVertexType> vbo, BufferObject<TIndexType> ebo)
    {
        _gl = gl;

        _handle = _gl.GenVertexArray();
        Bind();
        vbo.Bind();
        ebo.Bind();
    }
    
    // Like VertexAttributePointer, but advances once per instance (divisor 1) instead of
    // per vertex. The instance buffer must be bound to GL_ARRAY_BUFFER at call time so the
    // pointer associates with it. vertexSize/offSet are in TVertexType units.
    public unsafe void VertexAttributePointer(uint index, int count, VertexAttribPointerType type, uint vertexSize, int offSet)
    {
        _gl.VertexAttribPointer(index, count, type, false, vertexSize * (uint) sizeof(TVertexType), (void*) (offSet * sizeof(TVertexType)));
        _gl.EnableVertexAttribArray(index);
    }
    
    public unsafe void InstancedAttribute(uint index, int count, uint vertexSize, int offSet)
    {
        _gl.VertexAttribPointer(index, count, VertexAttribPointerType.Float, false, vertexSize * (uint) sizeof(TVertexType), (void*) (offSet * sizeof(TVertexType)));
        _gl.EnableVertexAttribArray(index);
        _gl.VertexAttribDivisor(index, 1);
    }


    public void Bind()
    {
        _gl.BindVertexArray(_handle);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_handle);
    }
}