namespace Centauri.Rendering.Renderers;

using Silk.NET.OpenGL;

using World;
using Graphics.Geometry;
using Graphics.Resources;
using Utils.Misc;

public class GridRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly GLShader _shader;
    private readonly Mesh _mesh;

    public GridRenderer(GL gl)
    {
        _gl     = gl;
        _shader = new GLShader(gl,
            PathResolver.Resolve("Assets/Shaders/Grid/grid.vert"),
            PathResolver.Resolve("Assets/Shaders/Grid/grid.frag"));
        
        float[] vertices =
        [
            // position       normal        uv      tangent
            -1f,  1f,  0f,  0f, 0f, 1f,  0f, 1f,  1f, 0f, 0f,
            -1f, -1f,  0f,  0f, 0f, 1f,  0f, 0f,  1f, 0f, 0f,
             1f,  1f,  0f,  0f, 0f, 1f,  1f, 1f,  1f, 0f, 0f,
             1f, -1f,  0f,  0f, 0f, 1f,  1f, 0f,  1f, 0f, 0f,
        ];

        uint[] indices = [0, 1, 2, 2, 1, 3];
        _mesh = new Mesh(gl, vertices, indices);
    }

    public void Render(Scene scene)
    {
        SetDebugRenderState();

        var camera = scene.Cameras.Active;
        
        _shader.Use();
        _shader.SetUniform("uView",        camera.GetViewMatrix());
        _shader.SetUniform("uProjection",  camera.GetProjectionMatrix());
        _shader.SetUniform("uCameraPos",   camera.Position);

        _mesh.Bind();
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, 6,
                DrawElementsType.UnsignedInt, (void*)0);
        }

        RestoreRenderState();
    }

    private void SetDebugRenderState()
    {
        _gl.DepthFunc(GLEnum.Lequal);
    }

    private void RestoreRenderState()
    {
        _gl.DepthFunc(DepthFunction.Less);
    }

    public void Dispose()
    {
        _mesh.Dispose();
        _shader.Dispose();
    }
}