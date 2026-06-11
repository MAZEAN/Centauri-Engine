namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;
using System.Numerics;

using Resources;
using Utils.Misc;

// Immediate-mode primitive drawer for the debug pass: owns the debug shader and a
// single growable dynamic buffer, and issues line/triangle/mesh draws. Knows nothing
// about cameras or bounding boxes.
public sealed class DebugDraw : IDisposable
{
    private readonly GL       _gl;
    private readonly GLShader _shader;

    private readonly uint _vao;
    private readonly uint _vbo;

    // current GPU buffer capacity in bytes — grows as needed, never shrinks
    private nuint _capacity;

    public DebugDraw(GL gl)
    {
        _gl = gl;
        _shader = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/debug.vert"),
            PathResolver.Resolve("Assets/Shaders/debug.frag"));

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float,
                false, 3 * sizeof(float), (void*)0);
        }
        _gl.BindVertexArray(0);
    }

    // ── pass setup / teardown ─────────────────────────────────────────────────
    public void Begin(Matrix4x4 view, Matrix4x4 projection)
    {
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);

        _shader.Use();
        _shader.SetUniform("uView",       view);
        _shader.SetUniform("uProjection", projection);
        _shader.SetUniform("uModel",      Matrix4x4.Identity);
        _shader.SetUniform("uAlpha",      1.0f);
    }

    public void End()
    {
        _gl.Enable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(true);
    }

    // ── per-draw uniforms ─────────────────────────────────────────────────────
    public void Color(Vector3 color, float alpha = 1.0f)
    {
        _shader.SetUniform("uColor", color);
        _shader.SetUniform("uAlpha", alpha);
    }

    public void Model(Matrix4x4 model) => _shader.SetUniform("uModel", model);

    // ── primitives ────────────────────────────────────────────────────────────
    public void Lines(float[] vertices)     => Upload(vertices, PrimitiveType.Lines);
    public void Triangles(float[] vertices) => Upload(vertices, PrimitiveType.Triangles);

    public void DrawMesh(Mesh mesh)
    {
        mesh.Bind();
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                DrawElementsType.UnsignedInt, (void*)0);
        }
    }

    private void Upload(float[] vertices, PrimitiveType mode)
    {
        var sizeInBytes = (nuint)(vertices.Length * sizeof(float));
        var count       = (uint)(vertices.Length / 3);

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        unsafe
        {
            fixed (float* v = vertices)
            {
                if (sizeInBytes > _capacity)
                {
                    _capacity = sizeInBytes * 2; // headroom to avoid frequent resizes
                    _gl.BufferData(BufferTargetARB.ArrayBuffer,
                        _capacity, null, BufferUsageARB.DynamicDraw);
                }

                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, sizeInBytes, v);
            }
        }

        _gl.DrawArrays(mode, 0, count);
    }

    public void EnableDepthMask() => _gl.DepthMask(true);
    public void DisableDepthMask() => _gl.DepthMask(false);

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}