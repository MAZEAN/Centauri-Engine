namespace Centauri.Rendering.DebugView;

using Silk.NET.OpenGL;

using Config;
using Graphics.Resources;
using Utils.Misc;

// Overlays a geometry-prepass buffer (view-space normals or linearized depth) over the
// whole screen, so the prepass output can be eyeballed before SSAO/SSR consume it.
// Draws straight to the backbuffer — call after the scene composite, before the UI.
public sealed class BufferDebugView : IDisposable
{
    private readonly GL _gl;
    private readonly GLShader _shader;
    private readonly uint _vao;   // core profile needs a bound VAO for attribute-less draws

    public BufferDebugView(GL gl)
    {
        _gl = gl;
        _shader = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Debug/buffer.vert"),
            PathResolver.Resolve("Assets/Shaders/Debug/buffer.frag"));
        _vao = gl.GenVertexArray();
    }

    public void Render(GBufferDebug mode, uint normalTex, uint depthTex, float near, float far)
    {
        if (mode == GBufferDebug.Off) return;

        _gl.Disable(EnableCap.DepthTest);

        _shader.Use();

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, normalTex);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, depthTex);

        _shader.SetUniform("uNormal", 0);
        _shader.SetUniform("uDepth",  1);
        _shader.SetUniform("uMode",   mode == GBufferDebug.Normals ? 1 : 2);
        _shader.SetUniform("uNear",   near);
        _shader.SetUniform("uFar",    far);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}